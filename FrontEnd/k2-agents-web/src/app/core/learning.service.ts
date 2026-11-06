import { Injectable, computed, inject, signal } from '@angular/core';
import { AuthService } from './auth/auth.service';
import { NotifyService } from './notify.service';

/**
 * Modo Iniciante: orientações contextuais (guias por módulo, termos explicados, tutorial do agente).
 * A preferência fica na conta do usuário, para valer em qualquer dispositivo; desligado por padrão.
 */
@Injectable({ providedIn: 'root' })
export class LearningService {
  private readonly auth = inject(AuthService);
  private readonly notify = inject(NotifyService);

  /** Valor otimista enquanto o servidor confirma a troca. */
  private readonly pending = signal<boolean | null>(null);
  readonly enabled = computed(() => this.pending() ?? this.auth.user()?.beginnerMode ?? false);

  toggle(): void {
    const next = !this.enabled();
    this.pending.set(next);
    this.auth.updateBeginnerMode(next).subscribe({
      next: () => {
        this.pending.set(null);
        this.notify.success(next ? 'learn.enabled' : 'learn.disabled');
      },
      error: (err) => {
        this.pending.set(null);
        this.notify.error(err);
      }
    });
  }
}
