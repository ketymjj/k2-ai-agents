import { HttpErrorResponse } from '@angular/common/http';
import { translate } from '@jsverse/transloco';

/** Extrai a mensagem amigável de um ProblemDetails da API (que já vem no idioma da interface). */
export function errorMessage(error: unknown, fallback?: string): string {
  if (error instanceof HttpErrorResponse) {
    if (error.status === 0) return translate('errors.network');
    const title = error.error?.title;
    if (typeof title === 'string' && title.length > 0) return title;
  }
  return fallback ?? translate('errors.unexpected');
}

/** Erros de validação por campo (chaves em minúsculo para casar com os nomes dos controles). */
export function fieldErrors(error: unknown): Record<string, string> {
  const result: Record<string, string> = {};
  if (error instanceof HttpErrorResponse && error.error?.errors) {
    for (const [key, messages] of Object.entries(error.error.errors as Record<string, string[]>)) {
      const name = key.charAt(0).toLowerCase() + key.slice(1);
      result[name] = messages[0];
    }
  }
  return result;
}
