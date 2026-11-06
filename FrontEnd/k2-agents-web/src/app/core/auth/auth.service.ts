import { HttpClient } from '@angular/common/http';
import { Injectable, computed, inject, signal } from '@angular/core';
import { Router } from '@angular/router';
import { Observable, catchError, finalize, map, of, shareReplay, tap, throwError } from 'rxjs';
import { AuthResponse, AuthUser, Language, Role } from '../models';
import { LanguageService } from '../language.service';

const API = '/api/v1/auth';

/**
 * O access token fica apenas em memória (nunca em localStorage).
 * O refresh token vive num cookie HttpOnly que o JavaScript não consegue ler.
 */
@Injectable({ providedIn: 'root' })
export class AuthService {
  private readonly http = inject(HttpClient);
  private readonly router = inject(Router);
  private readonly language = inject(LanguageService);

  private accessToken: string | null = null;
  private refreshTimer: ReturnType<typeof setTimeout> | null = null;
  private refreshInFlight: Observable<string | null> | null = null;

  readonly user = signal<AuthUser | null>(null);
  readonly isAuthenticated = computed(() => this.user() !== null);
  readonly role = computed(() => this.user()?.role ?? null);
  readonly isSuperAdmin = computed(() => this.role() === 'SuperAdmin');

  get token(): string | null {
    return this.accessToken;
  }

  hasRole(...roles: Role[]): boolean {
    const role = this.role();
    return role !== null && roles.includes(role);
  }

  login(email: string, password: string): Observable<AuthUser> {
    return this.http.post<AuthResponse>(`${API}/login`, { email, password }).pipe(
      tap((res) => this.applySession(res, true)),
      map((res) => res.user)
    );
  }

  /** Renova a sessão via cookie. Chamadas concorrentes compartilham a mesma requisição. */
  refresh(): Observable<string | null> {
    if (!this.refreshInFlight) {
      this.refreshInFlight = this.http.post<AuthResponse>(`${API}/refresh`, {}).pipe(
        tap((res) => this.applySession(res, false)),
        map((res) => res.accessToken),
        catchError((err) => {
          this.clearSession();
          return throwError(() => err);
        }),
        finalize(() => (this.refreshInFlight = null)),
        shareReplay(1)
      );
    }
    return this.refreshInFlight;
  }

  /** Executado na inicialização do app: restaura a sessão se o cookie ainda for válido. */
  restoreSession(): Observable<boolean> {
    return this.refresh().pipe(
      map(() => true),
      catchError(() => of(false))
    );
  }

  /** Aguarda o servidor revogar o refresh token; sem isso, um reload rápido restauraria a sessão. */
  logout(): void {
    this.http
      .post(`${API}/logout`, {})
      .pipe(finalize(() => {
        this.clearSession();
        this.router.navigate(['/login']);
      }))
      .subscribe({ error: () => undefined });
  }

  /** Sessão inválida detectada pelo interceptor: limpa e volta ao login preservando a rota. */
  expire(): void {
    this.clearSession();
    this.router.navigate(['/login'], { queryParams: { returnUrl: this.router.url, expired: 1 } });
  }

  changePassword(currentPassword: string, newPassword: string): Observable<void> {
    return this.http.post<void>(`${API}/change-password`, { currentPassword, newPassword });
  }

  updateLanguage(preferredLanguage: Language): Observable<AuthUser> {
    return this.http
      .put<AuthUser>(`${API}/me/preferences`, { preferredLanguage })
      .pipe(tap((user) => this.user.set(user)));
  }

  updateBeginnerMode(beginnerMode: boolean): Observable<AuthUser> {
    return this.http
      .put<AuthUser>(`${API}/me/preferences`, { beginnerMode })
      .pipe(tap((user) => this.user.set(user)));
  }

  private applySession(res: AuthResponse, fromLogin: boolean): void {
    this.accessToken = res.accessToken;
    this.user.set(res.user);
    if (fromLogin || !this.language.hasLocalChoice()) this.language.use(res.user.preferredLanguage);
    this.scheduleRefresh(res.expiresAt);
  }

  private scheduleRefresh(expiresAt: string): void {
    if (this.refreshTimer) clearTimeout(this.refreshTimer);
    // Renova 60s antes de expirar.
    const delay = Math.max(new Date(expiresAt).getTime() - Date.now() - 60_000, 5_000);
    this.refreshTimer = setTimeout(() => this.refresh().subscribe({ error: () => this.expire() }), delay);
  }

  private clearSession(): void {
    this.accessToken = null;
    this.user.set(null);
    if (this.refreshTimer) clearTimeout(this.refreshTimer);
    this.refreshTimer = null;
  }
}
