import { DatePipe } from '@angular/common';
import { Component, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { RouterLink } from '@angular/router';
import { EMPTY, catchError, switchMap, timer } from 'rxjs';
import { ApiError } from '../../core/api-error';
import { Order } from './order.model';
import { OrdersService } from './orders.service';

const POLL_INTERVAL_MS = 3000;

@Component({
  selector: 'app-order-list',
  imports: [DatePipe, RouterLink],
  template: `
    <section class="card">
      <div class="list-header">
        <h2>Pedidos</h2>
        <a class="button" routerLink="/pedidos/nuevo">Nuevo pedido</a>
      </div>

      @if (error(); as msg) {
        <div class="alert" role="alert">{{ msg }} <small>Reintentando automáticamente…</small></div>
      }

      @if (loading()) {
        <p>Cargando pedidos…</p>
      } @else if (items().length > 0) {
        <table>
          <thead>
            <tr><th>Cliente</th><th>SKU</th><th>Cantidad</th><th>Estado</th><th>Creado</th></tr>
          </thead>
          <tbody>
            @for (o of items(); track o.id) {
              <tr>
                <td>{{ o.clienteNombre }}</td>
                <td>{{ o.sku }}</td>
                <td>{{ o.cantidad }}</td>
                <td><span class="badge" [attr.data-status]="o.estadoOrden">{{ o.estadoOrden }}</span></td>
                <td>{{ o.fechaCreacion | date: 'dd/MM/yyyy HH:mm:ss' }}</td>
              </tr>
            }
          </tbody>
        </table>
      } @else if (!error()) {
        <p>Aún no hay pedidos. Crea el primero.</p>
      }

      @if (lastUpdate(); as t) {
        <small class="hint">Actualizado a las {{ t | date: 'HH:mm:ss' }} (cada 3 s)</small>
      }
    </section>
  `,
})
export class OrderListComponent {
  private readonly orders = inject(OrdersService);

  readonly items = signal<Order[]>([]);
  readonly loading = signal(true);
  readonly error = signal<string | null>(null);
  readonly lastUpdate = signal<Date | null>(null);

  constructor() {
    // timer + switchMap: si una petición tarda más que el intervalo se cancela la anterior (no se acumulan),
    // y el catchError interno evita que un fallo mate el polling.
    timer(0, POLL_INTERVAL_MS)
      .pipe(
        switchMap(() =>
          this.orders.list().pipe(
            catchError((err: ApiError) => {
              this.error.set(err.message);
              this.loading.set(false);
              return EMPTY;
            }),
          ),
        ),
        takeUntilDestroyed(),
      )
      .subscribe(list => {
        this.items.set(list);
        this.error.set(null);
        this.loading.set(false);
        this.lastUpdate.set(new Date());
      });
  }
}