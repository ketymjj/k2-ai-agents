import { HttpErrorResponse, HttpInterceptorFn, HttpRequest } from '@angular/common/http';
import { inject } from '@angular/core';
import { catchError, switchMap, throwError } from 'rxjs';
import { LanguageService } from '../language.service';
import { AuthService } from './auth.service';

const AUTH_ENDPOINTS = ['/api/v1/auth/login', '/api/v1/auth/refresh', '/api/v1/auth/logout'];

function withToken(req: HttpRequest<unknown>, token: string | null): HttpRequest<unknown> {
  return token ? req.clone({ setHeaders: { Authorization: `Bearer ${token}` } }) : req;
}

/**
 * Anexa o Bearer às chamadas da API e, em 401, tenta renovar a sessão uma única vez.
 * Envia também o idioma da interface, para as mensagens de erro do servidor virem no mesmo idioma.
 */
export const authInterceptor: HttpInterceptorFn = (incoming, next) => {
  if (!incoming.url.startsWith('/api/')) return next(incoming);

  const req = incoming.clone({ setHeaders: { 'Accept-Language': inject(LanguageService).current() } });
  const auth = inject(AuthService);
  if (AUTH_ENDPOINTS.some((url) => req.url.startsWith(url))) return next(req);

  return next(withToken(req, auth.token)).pipe(
    catchError((error: unknown) => {
      if (!(error instanceof HttpErrorResponse) || error.status !== 401 || !auth.isAuthenticated()) {
        return throwError(() => error);
      }
      return auth.refresh().pipe(
        switchMap((token) => next(withToken(req, token))),
        catchError((refreshError) => {
          auth.expire();
          return throwError(() => refreshError);
        })
      );
    })
  );
};
