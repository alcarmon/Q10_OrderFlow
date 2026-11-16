import { HttpErrorResponse } from '@angular/common/http';
import { provideHttpClient, withInterceptors } from '@angular/common/http';

export interface ApiError {
  status: number;
  message: string;
  fieldErrors: Record<string, string[]>;
}

export function toApiError(err: HttpErrorResponse): ApiError {
  if (err.status === 0) {
    return {
      status: 0,
      message: 'No se pudo conectar con el servidor. Verifica que el backend esté en ejecución.',
      fieldErrors: {},
    };
  }
  if (err.status === 400 && err.error?.errors) {
    return { status: 400, message: 'Hay datos inválidos en el formulario.', fieldErrors: err.error.errors };
  }
  if (err.status === 400) {
    return { status: 400, message: err.error?.title ?? 'La solicitud no es válida.', fieldErrors: {} };
  }
  if (err.status >= 500) {
    return { status: err.status, message: 'El servidor tuvo un error. Intenta de nuevo en unos segundos.', fieldErrors: {} };
  }
  return { status: err.status, message: `Error inesperado (${err.status}).`, fieldErrors: {} };
}