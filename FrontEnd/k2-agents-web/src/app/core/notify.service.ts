import { Injectable, inject } from '@angular/core';
import { MatSnackBar } from '@angular/material/snack-bar';
import { TranslocoService } from '@jsverse/transloco';
import { errorMessage } from './http-error';

@Injectable({ providedIn: 'root' })
export class NotifyService {
  private readonly snack = inject(MatSnackBar);
  private readonly transloco = inject(TranslocoService);

  /** Mensagem de sucesso a partir de uma chave de tradução. */
  success(key: string, params?: Record<string, unknown>): void {
    this.snack.open(this.transloco.translate(key, params), '✕', { duration: 3500, panelClass: 'k2-snack--success' });
  }

  error(error: unknown): void {
    const message = errorMessage(error, this.transloco.translate('errors.unexpected'));
    this.snack.open(message, '✕', { duration: 6000, panelClass: 'k2-snack--error' });
  }
}
