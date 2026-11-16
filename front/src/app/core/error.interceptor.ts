import { HttpErrorResponse, HttpInterceptorFn } from '@angular/common/http';
import { catchError, throwError } from 'rxjs';
import { toApiError } from './api-error';


/** Convierte cualquier error HTTP en un ApiError tipado: los componentes nunca ven HttpErrorResponse. */
export const errorInterceptor: HttpInterceptorFn = (req, next) =>
  next(req).pipe(
    catchError((err: unknown) =>
      throwError(() => (err instanceof HttpErrorResponse ? toApiError(err) : err)),
    ),
  );