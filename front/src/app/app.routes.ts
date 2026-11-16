import { Routes } from '@angular/router';

export const routes: Routes = [
  { path: '', pathMatch: 'full', redirectTo: 'pedidos' },
  {
    path: 'pedidos',
   loadComponent: () => import('./features/orders/order-list.component').then(m => m.OrderListComponent),
  },
  {
    path: 'pedidos/nuevo',
    loadComponent: () => import('./features/orders/order-form.component').then(m => m.OrderFormComponent),
  },
  { path: '**', redirectTo: 'pedidos' },
];