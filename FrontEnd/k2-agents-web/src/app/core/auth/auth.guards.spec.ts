import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { ActivatedRouteSnapshot, Router, RouterStateSnapshot, UrlTree, provideRouter } from '@angular/router';
import { AuthUser, Role } from '../models';
import { authGuard, homeRedirectGuard, roleGuard } from './auth.guards';
import { AuthService } from './auth.service';

function fakeAuth(role: Role | null, companyId: string | null = 'c1') {
  const user = signal<AuthUser | null>(
    role ? { id: '1', name: 'X', email: 'x@y.z', role, companyId, companyName: null, preferredLanguage: 'pt-BR', beginnerMode: false } : null
  );
  return {
    user,
    isAuthenticated: () => user() !== null,
    hasRole: (...roles: Role[]) => !!user() && roles.includes(user()!.role)
  };
}

function run(guard: typeof authGuard, auth: ReturnType<typeof fakeAuth>, data: Record<string, unknown> = {}) {
  TestBed.configureTestingModule({ providers: [provideRouter([]), { provide: AuthService, useValue: auth }] });
  const route = { data } as unknown as ActivatedRouteSnapshot;
  const result = TestBed.runInInjectionContext(() => guard(route, { url: '/users' } as RouterStateSnapshot));
  const router = TestBed.inject(Router);
  return result instanceof UrlTree ? router.serializeUrl(result) : result;
}

describe('auth guards', () => {
  it('authGuard envia anônimos ao login preservando a rota', () => {
    expect(run(authGuard, fakeAuth(null))).toBe('/login?returnUrl=%2Fusers');
  });

  it('roleGuard bloqueia perfis não autorizados', () => {
    expect(run(roleGuard, fakeAuth('Viewer'), { roles: ['SuperAdmin', 'CompanyAdmin'] })).toBe('/');
  });

  it('roleGuard libera perfis autorizados', () => {
    expect(run(roleGuard, fakeAuth('CompanyAdmin'), { roles: ['SuperAdmin', 'CompanyAdmin'] })).toBeTrue();
  });

  it('home leva todos os perfis ao painel', () => {
    expect(run(homeRedirectGuard, fakeAuth('Attendant', 'abc'))).toBe('/dashboard');
    TestBed.resetTestingModule();
    expect(run(homeRedirectGuard, fakeAuth('SuperAdmin', null))).toBe('/dashboard');
  });
});
