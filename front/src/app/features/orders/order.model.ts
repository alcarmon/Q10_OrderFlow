export type OrderStatus = 'Pending' | 'Confirmed' | 'Rejected';

export interface Order {
  id: string;
  clienteNombre: string;
  sku: string;
  cantidad: number;
  estadoOrden: OrderStatus;
  fechaCreacion: string;
}

export interface CreateOrderRequest {
  clienteNombre: string;
  sku: string;
  cantidad: number;
}