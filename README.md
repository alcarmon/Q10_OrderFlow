# OrderFlow

Sistema de pedidos distribuido: un panel web (Angular), una **Orders API** y un **Inventory Worker** (.NET 8) que se comunican por eventos con **RabbitMQ**, sobre **SQL Server**. Todo levanta con un solo comando.

Un pedido nace `Pending`, Inventory intenta reservar el stock y el pedido termina `Confirmed` (hay stock) o `Rejected` (no hay).

## Inicio rápido

**Requisitos:** Docker Desktop con al menos 4 GB de RAM asignados (SQL Server exige mínimo 2 GB) y estos puertos libres: `8081`, `8080`, `5672`, `15672` y `14333`.

```bash
docker compose up --build
```

La primera vez tarda unos minutos (imágenes, NuGet y npm). No hay pasos manuales: las migraciones y el seed se aplican solos al arrancar.

| Qué | Dónde |
|---|---|
| Panel web | http://localhost:8081 |
| Orders API | http://localhost:8080/orders |
| RabbitMQ (administración) | http://localhost:15672 |
| SQL Server (para SSMS u otro cliente) | `localhost,14333` |

Las credenciales por defecto (`sa` / `orderflow` con la contraseña `OrderFlow_Dev2026`) son **solo para desarrollo local** y se sobreescriben con variables de entorno (ver [Configuración](#configuración)). Para reiniciar desde cero: `docker compose down -v` y luego `docker compose up`.

**Datos de prueba** (`seed/products.json`): `ABC-01` (100 unidades), `ABC-02` (50) y `ABC-03` (10). Pedir 11 unidades de `ABC-03` termina en `Rejected`.

## Cómo probar el flujo

1. Abre http://localhost:8081 y pulsa **Nuevo pedido**.
2. Crea `ABC-01` × 2: aparece `Pending` y, en unos segundos, pasa solo a `Confirmed` (la lista se refresca cada 3 s).
3. Crea `ABC-03` × 11: termina en `Rejected`.
4. Envía el formulario con datos inválidos (nombre vacío, cantidad 0 o un SKU inexistente como `NOEXISTE`): los errores se muestran en pantalla, junto a cada campo.

### Demostrar la idempotencia

El outbox permite forzar que Orders republique un evento ya procesado:

```bash
# 1) Stock actual
docker compose exec sqlserver /opt/mssql-tools18/bin/sqlcmd -C -S localhost -U sa -P OrderFlow_Dev2026 -Q "SELECT * FROM InventoryDb.dbo.Stock"

# 2) Republicar el último evento OrderCreated
docker compose exec sqlserver /opt/mssql-tools18/bin/sqlcmd -C -S localhost -U sa -P OrderFlow_Dev2026 -Q "UPDATE OrdersDb.dbo.Outbox SET PublicadoEn = NULL WHERE Id = (SELECT TOP 1 Id FROM OrdersDb.dbo.Outbox ORDER BY CreadoEn DESC)"

# 3) Inventory lo detecta como duplicado
docker compose logs inventory-worker --tail 5

# 4) El stock no cambió respecto al paso 1
docker compose exec sqlserver /opt/mssql-tools18/bin/sqlcmd -C -S localhost -U sa -P OrderFlow_Dev2026 -Q "SELECT * FROM InventoryDb.dbo.Stock"
```

El log de Inventory dirá `[DUPLICADO: stock NO descontado de nuevo]`.

### Demostrar los fallos

```bash
docker compose stop inventory-worker   # crea un pedido: queda Pending
docker compose start inventory-worker  # el pedido pasa a Confirmed

docker compose stop rabbitmq           # crea un pedido: la API responde 201 igualmente
docker compose start rabbitmq          # el mensaje pendiente en el outbox se publica solo
```

## Tests

```bash
cd back
dotnet test
```

Son 5 tests. Detén antes cualquier servicio local en ejecución: si no, el build avisa de DLL bloqueados (no es un fallo).

| Proyecto | Test | Qué verifica |
|---|---|---|
| Orders.Tests | Request inválido | Un error por campo (nombre, SKU, cantidad) |
| Orders.Tests | Request válido en los límites | Cantidad 1 y 100 se aceptan |
| Orders.Tests | Transiciones del pedido | Solo se sale de `Pending`; un evento duplicado o contradictorio se ignora |
| Inventory.Tests | Mismo evento dos veces | El stock se descuenta **una sola vez** |
| Inventory.Tests | Stock insuficiente | Se rechaza y el stock no cambia |

Los tests de Inventory usan SQLite en memoria (ver trade-offs).

## Arquitectura

```
                      ┌──────────────────────── RabbitMQ ───────────────────────┐
                      │   OrderCreated  ─────────────────►  cola order-created  │
Panel (Angular)       │                                                         │
   │  /api (nginx)    │   cola order-stock-result  ◄─────  StockReserved /      │
   ▼                  │                                     StockRejected       │
Orders API ───────────┘                                         ▲               │
 │  POST /orders: pedido + Outbox (1 transacción)               │               │
 │  OutboxDispatcher publica OrderCreated                Inventory Worker       │
 │  Consumer aplica el resultado al pedido                │ ProcessedEvents +   │
 ▼                                                        ▼ descuento atómico   │
OrdersDb  (SQL Server)                                InventoryDb (SQL Server)
```

| Componente | Responsabilidad |
|---|---|
| **Panel (Angular + nginx)** | Formulario de alta con validaciones y lista con polling cada 3 s. nginx sirve el front y reenvía `/api` a Orders.API |
| **Orders API** | REST (`POST/GET /orders`, `GET /orders/{id}`), valida, guarda el pedido `Pending` junto a un mensaje en la tabla `Outbox`, publica `OrderCreated` y aplica `StockReserved` / `StockRejected` |
| **Inventory Worker** | Consume `OrderCreated`, descuenta stock de forma atómica e idempotente y publica `StockReserved` o `StockRejected` |
| **RabbitMQ** | Transporte asíncrono entre los servicios |
| **SQL Server** | Una instancia con dos bases: `OrdersDb` e `InventoryDb` |

### Contratos

| Endpoint | Resultado |
|---|---|
| `POST /orders` | `201` con el pedido y cabecera `Location`; `400` con `errors` por campo si los datos son inválidos |
| `GET /orders` | Lista de pedidos, del más reciente al más antiguo |
| `GET /orders/{id}` | Detalle; `404` si no existe |

Pedido: `{ id, clienteNombre, sku, cantidad, estadoOrden, fechaCreacion }`, con `estadoOrden` en `Pending | Confirmed | Rejected`. Validación: `clienteNombre` no vacío, `sku` existente en el catálogo y `cantidad` entre 1 y 100.

Eventos (proyecto `OrderFlow.Contracts`): `OrderCreated`, `StockReserved` y `StockRejected`. Todos llevan `eventId`, `orderId`, `sku`, `cantidad` y `ocurridoEn` (`StockRejected` añade `motivo`).

## Decisiones de arquitectura

- **Capas ligeras, no Clean Architecture completa.** El dominio es pequeño (tres endpoints, una entidad, un caso de uso por servicio). Hay 5 proyectos y la separación se hace por carpetas dentro de cada servicio (`Domain`, `Infrastructure`, `Endpoints`/`Messaging`). Las reglas de negocio viven en el dominio (`Order`) y en una función pura de validación; no hay repositorios sobre EF ni MediatR, porque `DbContext` ya es unidad de trabajo.
- **Cada servicio es dueño de sus datos.** Orders e Inventory tienen bases separadas y solo se coordinan por eventos. Eso hace honesto el uso del broker y permite evolucionar cada esquema por separado.
- **Outbox transaccional en Orders.** Guardar el pedido y publicar el evento son dos sistemas sin transacción común. Por eso el pedido y su mensaje se guardan juntos en `Outbox`, y un `BackgroundService` publica y marca los mensajes. La entrega es *al menos una vez*.
- **Idempotencia en Inventory (bandeja de entrada).** La tabla `ProcessedEvents` (PK = `EventId`) se escribe en la misma transacción que el descuento y guarda el **resultado**. Un duplicado no descuenta, pero **republica** el mismo resultado, de modo que si la app cayó entre confirmar y publicar el pedido no se queda atascado. La PK única cubre además la carrera de dos consumidores sobre el mismo evento.
- **Descuento atómico.** `UPDATE ... SET Disponible = Disponible - @n WHERE Sku = @sku AND Disponible >= @n`: no hay lectura-modificación-escritura, y una restricción `CHECK (Disponible >= 0)` respalda el invariante en la base.
- **Orders es idempotente por su máquina de estados.** `Confirm()` y `Reject()` solo actúan sobre un pedido `Pending`, así que repetir un evento es inocuo, sin tabla adicional. Un token de concurrencia (`RowVersion`) evita que dos mensajes simultáneos pisen el estado.
- **MassTransit solo como transporte.** Aporta conexión, topología y reintentos. El outbox y la idempotencia están escritos a mano para que la lógica de consistencia sea explícita.
- **El rechazo por stock es un estado, no un error de validación.** `POST /orders` solo devuelve 400 por datos inválidos; la creación nunca depende de que Inventory esté disponible.
- **Front.** Componentes standalone con signals. Un servicio HTTP y un interceptor que convierte cualquier error en un `ApiError` tipado; los errores 400 del servidor se muestran bajo el campo correspondiente. El polling usa `timer` + `switchMap`: no acumula peticiones y un fallo no lo detiene.
- **Docker.** Builds multi-stage (SDK para compilar; `aspnet`/`runtime` para ejecutar, sin SDK, con usuario no root) con restauración en una capa aparte para aprovechar la caché. Imágenes Debian y no Alpine, porque el cliente de SQL Server necesita ICU.
- **SQL Server.** Es el motor principal de la plataforma de destino. Se levanta en compose con healthcheck y volumen, y los servicios esperan con `depends_on: service_healthy`.

## Manejo de fallos

| Escenario | Qué pasa |
|---|---|
| **RabbitMQ caído al crear un pedido** | La API responde `201`; el pedido queda `Pending` y su mensaje queda sin publicar en `Outbox`. El dispatcher reintenta cada 2 s (timeout de 10 s por publicación) y publica cuando el broker vuelve |
| **Inventory caído** | Los `OrderCreated` esperan en una cola durable y los pedidos siguen `Pending`. Al volver Inventory se procesan todos |
| **Inventory falla procesando un mensaje** | 5 reintentos (cada 2 s); después el mensaje va a la cola `_error` del endpoint |
| **Evento duplicado** | `ProcessedEvents` evita el doble descuento y se republica el resultado ya guardado |
| **Caída entre confirmar la transacción y publicar** | El broker reentrega el mensaje; se detecta como duplicado y se republica el resultado |
| **Caída del dispatcher entre publicar y marcar** | El evento se publica otra vez (al menos una vez) y la idempotencia del consumidor lo absorbe |
| **Resultado duplicado o tardío en Orders** | La transición solo ocurre desde `Pending`; el resto se ignora |
| **Stock insuficiente o SKU desconocido en Inventory** | El pedido termina `Rejected` con un motivo; no es una excepción |
| **SQL Server aún no acepta conexiones** | Healthcheck en compose y reintentos de la migración al arrancar (hasta 30 × 2 s) |

## Trade-offs asumidos

- **Consistencia eventual:** el pedido pasa unos segundos por `Pending`. Es el precio de no usar una transacción distribuida.
- **Catálogo duplicado:** Orders conserva su propia lista de SKU válidos (para devolver 400 sin acoplarse a Inventory) e Inventory las cantidades. Ambos leen el mismo `seed/products.json`. Si un producto existe en Inventory pero no en Orders, Orders lo rechaza hasta que se sincronice.
- **La cola la declara el consumidor.** Si Orders publicara antes de que Inventory arrancara alguna vez, el mensaje se descartaría. En compose ambos arrancan antes de que haya pedidos.
- **Tests de Inventory con SQLite en memoria:** corren sin Docker y ejecutan el SQL real del servicio (transacción, `ExecuteUpdate`, PK única), pero no reproducen el error 2627 de SQL Server ni la concurrencia real, así que la rama de carrera no está cubierta.
- **Usuario `sa` para los servicios:** simplifica el compose. En producción usaría un login por servicio con permisos mínimos.
- **Un solo dispatcher de outbox:** con varias instancias de Orders podrían tomar los mismos mensajes (el consumidor lo toleraría, pero se duplicaría tráfico).
- **Polling en vez de tiempo real:** más simple y suficiente; SignalR queda como mejora.
- **Swagger solo en `Development`:** el contenedor corre en `Production`, así que no lo expone.
- **Sin autenticación:** el enunciado no la exige. La API, el panel y el broker quedan abiertos dentro del entorno local (ver "Seguridad" más abajo para el plan).

## Configuración

Todo por variables de entorno; no hay secretos ni cadenas de conexión en el código. En `docker-compose.yml` los valores por defecto sirven solo para desarrollo local y se sobreescriben con un archivo `.env` (ver `.env.example`) o con variables del sistema.

| Variable | Para qué | Por defecto (solo desarrollo) |
|---|---|---|
| `SQL_SA_PASSWORD` | Contraseña de `sa` en SQL Server y en las cadenas de conexión | `OrderFlow_Dev2026` |
| `RABBITMQ_USER` / `RABBITMQ_PASS` | Credenciales de RabbitMQ | `orderflow` / `OrderFlow_Dev2026` |
| `SQL_PORT` | Puerto de SQL Server en tu máquina | `14333` |
| `FRONT_PORT` | Puerto del panel en tu máquina | `8081` |
| `ConnectionStrings__OrdersDb` | Conexión de Orders.Api | la define el compose |
| `ConnectionStrings__InventoryDb` | Conexión de Inventory.Worker | la define el compose |
| `RabbitMq__Host` / `__Username` / `__Password` | Conexión al broker | la define el compose |
| `Seed__ProductsPath` | Ruta del JSON de seed | `/seed/products.json` |
| `ORDERS_API_URL` | Destino del proxy `/api` en nginx | `http://orders-api:8080` |

Si cambias las contraseñas, evita los caracteres `;`, `$` y las comillas, y recuerda que con un volumen de SQL Server ya creado el cambio no aplica hasta ejecutar `docker compose down -v`.

## Desarrollo local sin Docker

Requisitos: SDK de .NET 8 o superior, Node.js 22.12 o superior, una instancia de SQL Server accesible y RabbitMQ (por ejemplo, con `docker compose up -d rabbitmq`).

```bash
# Backend: configura los secretos una vez por proyecto
cd back/src/Orders.Api
dotnet user-secrets set "ConnectionStrings:OrdersDb" "<tu cadena de conexión>"
dotnet user-secrets set "RabbitMq:Username" "<usuario>"
dotnet user-secrets set "RabbitMq:Password" "<password>"
dotnet run

cd ../Inventory.Worker
dotnet user-secrets set "ConnectionStrings:InventoryDb" "<tu cadena de conexión>"
dotnet user-secrets set "RabbitMq:Username" "<usuario>"
dotnet user-secrets set "RabbitMq:Password" "<password>"
dotnet user-secrets set "Seed:ProductsPath" "<ruta absoluta a seed/products.json>"
dotnet run

# Front (proxy de desarrollo hacia Orders.Api; ajusta el puerto en front/proxy.conf.json)
cd front
npm ci
npm start   # http://localhost:4200
```

Swagger queda disponible en Orders.Api cuando corre en `Development`.

## Qué haría distinto con más tiempo

- **Tiempo real con SignalR** en lugar de polling (y, mientras tanto, pausar el polling cuando la pestaña esté oculta).
- **Seguridad.** Hoy no hay autenticación ni autorización (el enunciado no las pide), así que la API y el panel deben usarse solo en una red de confianza. Con más tiempo:
  - *Panel → Orders API:* autenticación con **JWT**, emitido por un proveedor de identidad compatible con OIDC y validado en la API (`AddJwtBearer`), con roles (por ejemplo, el equipo de operaciones). nginx reenviaría la cabecera `Authorization` y el front adjuntaría el token con un interceptor HTTP.
  - *Entre servicios:* hoy no hay llamadas HTTP entre ellos, solo mensajería, por lo que JWT no aplica en ese canal. Se aseguraría con un usuario de RabbitMQ por servicio con permisos mínimos, TLS (`amqps`) en el broker y en SQL Server (sin `TrustServerCertificate`) y secretos gestionados fuera del compose. Si más adelante aparecieran llamadas HTTP síncronas entre servicios (por ejemplo, un servicio de catálogo), usaría JWT con *client credentials* o mTLS.
  - *Base de datos:* un login de SQL por servicio con permisos mínimos, en lugar de `sa`.
- **Tests de integración con Testcontainers (SQL Server real):** cubrir la carrera de dos consumidores sobre el mismo evento y el flujo completo del outbox. Añadir tests del front.
- **Outbox más robusto:** contador de intentos y estado "fallido" para mensajes que no se pueden publicar, y bloqueo de filas (`UPDLOCK, READPAST`) para escalar a varias instancias.
- **Monitoreo de las colas `_error`** y reproceso controlado.
- **Sincronizar el catálogo por eventos** (`ProductRegistered`) y un endpoint administrativo para altas y reposición de stock (`StockAdjusted`), eliminando la duplicación del catálogo.
- **Topología declarada en ambos lados** (o publicación `mandatory`) para no perder mensajes si el consumidor nunca arrancó.
- **Observabilidad:** ids de correlación en los logs, métricas y trazas (OpenTelemetry).
- **Endpoints `/health`** y manifiestos de Kubernetes (Deployment, Service y ConfigMap con probes y límites de recursos).
- **Paginación y filtros** en `GET /orders`.
- **CI** con build, tests y construcción de imágenes.

## Estructura del repositorio

```
.
├── docker-compose.yml
├── .env.example
├── seed/products.json            # catálogo inicial (Orders: SKU válidos; Inventory: cantidades)
├── back/
│   ├── OrderFlow.sln
│   ├── Dockerfile.orders
│   ├── Dockerfile.inventory
│   ├── src/
│   │   ├── OrderFlow.Contracts/  # eventos compartidos
│   │   ├── Orders.Api/           # Domain, Endpoints, Infrastructure (EF, outbox), Messaging
│   │   └── Inventory.Worker/     # Domain, Services (reserva idempotente), Infrastructure, Messaging
│   └── tests/
│       ├── Orders.Tests/
│       └── Inventory.Tests/
└── front/                        # Angular (core, features/orders), Dockerfile y plantilla de nginx
```
