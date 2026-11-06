import { inject } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';
import { Role } from '../models';
import { AuthService } from './auth.service';

export const authGuard: CanActivateFn = (_route, state) => {
  const auth = inject(AuthService);
  return auth.isAuthenticated() ? true : inject(Router).createUrlTree(['/login'], { queryParams: { returnUrl: state.url } });
};

export const guestGuard: CanActivateFn = () => {
  const auth = inject(AuthService);
  return auth.isAuthenticated() ? inject(Router).createUrlTree(['/']) : true;
};

/** Restringe a rota aos perfis em `data.roles`. A API aplica a mesma regra; isto é só UX. */
export const roleGuard: CanActivateFn = (route) => {
  const roles = (route.data['roles'] as Role[] | undefined) ?? [];
  return inject(AuthService).hasRole(...roles) ? true : inject(Router).createUrlTree(['/']);
};

/** Página inicial por perfil: plataforma vê todas as empresas; demais, a própria empresa. */
export const homeRedirectGuard: CanActivateFn = () => {
  const auth = inject(AuthService);
  const router = inject(Router);
  const user = auth.user();
  if (!user) return router.createUrlTree(['/login']);
  return router.createUrlTree(['/dashboard']);
};
