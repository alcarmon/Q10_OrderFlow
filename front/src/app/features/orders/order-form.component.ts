import { Component, inject, signal } from '@angular/core';
import { NonNullableFormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { ApiError } from '../../core/api-error';
import { OrdersService } from './orders.service';

type FieldName = 'clienteNombre' | 'sku' | 'cantidad';

@Component({
  selector: 'app-order-form',
  imports: [ReactiveFormsModule, RouterLink],
  template: `
    <section class="card">
      <h2>Nuevo pedido</h2>

      @if (generalErrors().length > 0) {
        <div class="alert" role="alert">
          @for (msg of generalErrors(); track msg) { <p>{{ msg }}</p> }
        </div>
      }

      <form [formGroup]="form" (ngSubmit)="submit()" novalidate>
        <label>
          Cliente
          <input formControlName="clienteNombre" autocomplete="off" />
          @if (errorOf('clienteNombre'); as msg) { <small class="field-error">{{ msg }}</small> }
        </label>

        <label>
          SKU
          <input formControlName="sku" autocomplete="off" placeholder="ABC-01" />
          @if (errorOf('sku'); as msg) { <small class="field-error">{{ msg }}</small> }
        </label>

        <label>
          Cantidad (1 a 100)
          <input type="number" formControlName="cantidad" />
          @if (errorOf('cantidad'); as msg) { <small class="field-error">{{ msg }}</small> }
        </label>

        <div class="actions">
          <button type="submit" [disabled]="submitting()">
            {{ submitting() ? 'Enviando…' : 'Crear pedido' }}
          </button>
          <a routerLink="/pedidos">Cancelar</a>
        </div>
      </form>
    </section>
  `,
})
export class OrderFormComponent {
  private readonly fb = inject(NonNullableFormBuilder);
  private readonly orders = inject(OrdersService);
  private readonly router = inject(Router);

  readonly submitting = signal(false);
  readonly generalErrors = signal<string[]>([]);

  // Mismas reglas que el backend (el servidor sigue siendo la fuente de verdad)
  readonly form = this.fb.group({
    clienteNombre: ['', [Validators.required, Validators.pattern(/\S/), Validators.maxLength(200)]],
    sku: ['', [Validators.required, Validators.pattern(/\S/), Validators.maxLength(50)]],
    cantidad: [1, [Validators.required, Validators.min(1), Validators.max(100)]],
  });

  submit(): void {
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }

    this.submitting.set(true);
    this.generalErrors.set([]);

    this.orders.create(this.form.getRawValue()).subscribe({
      next: () => this.router.navigate(['/pedidos']),
      error: (err: ApiError) => {
        this.submitting.set(false);
        this.showServerError(err);
      },
    });
  }

  errorOf(name: FieldName): string | null {
    const control = this.form.controls[name];
    if (!control.touched || !control.errors) return null;

    if (control.errors['server']) return control.errors['server'];
    if (control.errors['required'] || control.errors['pattern']) {
      return name === 'cantidad' ? 'Indica una cantidad.' : 'Este campo es obligatorio.';
    }
    if (control.errors['min'] || control.errors['max']) return 'La cantidad debe estar entre 1 y 100.';
    if (control.errors['maxlength']) return 'El texto es demasiado largo.';
    return 'Valor inválido.';
  }

  // Los errores 400 del servidor se pintan bajo el campo correspondiente; el resto, en el banner.
  private showServerError(err: ApiError): void {
    const general: string[] = [err.message];

    for (const [key, messages] of Object.entries(err.fieldErrors)) {
      const controlName = Object.keys(this.form.controls).find(k => k.toLowerCase() === key.toLowerCase());
      if (controlName) {
        const control = this.form.get(controlName)!;
        control.setErrors({ server: messages.join(' ') }); // se borra solo al editar el campo
        control.markAsTouched();
      } else {
        general.push(...messages);
      }
    }

    this.generalErrors.set(general);
  }
}