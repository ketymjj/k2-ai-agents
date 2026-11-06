import { AbstractControl, ValidationErrors } from '@angular/forms';

/** Mesma política do backend: 8+ caracteres, maiúscula, minúscula e número. */
export const STRONG_PASSWORD = /^(?=.*[a-z])(?=.*[A-Z])(?=.*\d).{8,128}$/;

export function httpUrl(control: AbstractControl): ValidationErrors | null {
  const value = (control.value ?? '').toString().trim();
  if (!value) return null;
  try {
    const url = new URL(value);
    return url.protocol === 'http:' || url.protocol === 'https:' ? null : { url: true };
  } catch {
    return { url: true };
  }
}

/** Converte string vazia em null antes de enviar à API. */
export function emptyToNull<T extends Record<string, unknown>>(value: T): T {
  const result: Record<string, unknown> = {};
  for (const [key, v] of Object.entries(value)) result[key] = typeof v === 'string' && v.trim() === '' ? null : v;
  return result as T;
}
