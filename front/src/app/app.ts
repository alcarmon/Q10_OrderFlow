import { Component } from '@angular/core';
import { RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';

@Component({
  selector: 'app-root',
  imports: [RouterOutlet, RouterLink, RouterLinkActive],
  template: `
    <header class="topbar">
      <h1>OrderFlow</h1>
      <nav>
        <a routerLink="/pedidos" routerLinkActive="active" [routerLinkActiveOptions]="{ exact: true }">Pedidos</a>
        <a routerLink="/pedidos/nuevo" routerLinkActive="active">Nuevo pedido</a>
      </nav>
    </header>
    <main><router-outlet /></main>
  `,
})
export class App {}