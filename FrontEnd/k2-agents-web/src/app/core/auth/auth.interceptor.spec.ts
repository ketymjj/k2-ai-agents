import { HttpClient, provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { TranslocoTestingModule } from '@jsverse/transloco';
import { LanguageService } from '../language.service';
import { AuthResponse } from '../models';
import { authInterceptor } from './auth.interceptor';
import { AuthService } from './auth.service';

const session = (token: string): AuthResponse => ({
  accessToken: token,
  expiresAt: new Date(Date.now() + 15 * 60_000).toISOString(),
  user: { id: '1', name: 'Ana', email: 'a@b.c', role: 'CompanyAdmin', companyId: 'c1', companyName: 'Padaria', preferredLanguage: 'pt-BR', beginnerMode: false }
});

describe('authInterceptor', () => {
  let http: HttpClient;
  let backend: HttpTestingController;
  let auth: AuthService;

  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [TranslocoTestingModule.forRoot({ langs: {}, translocoConfig: { availableLangs: ['pt-BR', 'en'], defaultLang: 'pt-BR' } })],
      providers: [provideRouter([]), provideHttpClient(withInterceptors([authInterceptor])), provideHttpClientTesting()]
    });
    http = TestBed.inject(HttpClient);
    backend = TestBed.inject(HttpTestingController);
    auth = TestBed.inject(AuthService);

    auth.login('a@b.c', 'x').subscribe();
    backend.expectOne('/api/v1/auth/login').flush(session('token-1'));
  });

  afterEach(() => backend.verify());

  it('anexa o Bearer apenas às chamadas da API', () => {
    http.get('/api/v1/companies').subscribe();
    http.get('/i18n/pt-BR.json').subscribe();

    expect(backend.expectOne('/api/v1/companies').request.headers.get('Authorization')).toBe('Bearer token-1');
    expect(backend.expectOne('/i18n/pt-BR.json').request.headers.has('Authorization')).toBeFalse();
  });

  it('envia o idioma da interface para o servidor responder no mesmo idioma', () => {
    TestBed.inject(LanguageService).use('en');
    http.get('/api/v1/companies').subscribe();
    expect(backend.expectOne('/api/v1/companies').request.headers.get('Accept-Language')).toBe('en');
  });

  it('em 401 renova a sessão uma vez e repete a requisição com o novo token', () => {
    let body: unknown;
    http.get('/api/v1/companies').subscribe((r) => (body = r));

    backend.expectOne('/api/v1/companies').flush(null, { status: 401, statusText: 'Unauthorized' });
    backend.expectOne('/api/v1/auth/refresh').flush(session('token-2'));
    const retry = backend.expectOne('/api/v1/companies');
    expect(retry.request.headers.get('Authorization')).toBe('Bearer token-2');
    retry.flush({ ok: true });

    expect(body).toEqual({ ok: true });
  });

  it('encerra a sessão quando o refresh falha', () => {
    http.get('/api/v1/companies').subscribe({ error: () => undefined });

    backend.expectOne('/api/v1/companies').flush(null, { status: 401, statusText: 'Unauthorized' });
    backend.expectOne('/api/v1/auth/refresh').flush(null, { status: 401, statusText: 'Unauthorized' });

    expect(auth.isAuthenticated()).toBeFalse();
    expect(auth.token).toBeNull();
  });

  it('nunca grava o access token no localStorage', () => {
    expect(JSON.stringify(localStorage)).not.toContain('token-1');
  });
});
