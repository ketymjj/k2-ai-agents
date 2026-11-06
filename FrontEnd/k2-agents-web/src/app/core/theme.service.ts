import { DOCUMENT } from '@angular/common';
import { Injectable, inject, signal } from '@angular/core';

export type Theme = 'light' | 'dark';
const STORAGE_KEY = 'k2.theme';

@Injectable({ providedIn: 'root' })
export class ThemeService {
  private readonly document = inject(DOCUMENT);
  readonly theme = signal<Theme>(this.initial());

  constructor() {
    this.apply(this.theme());
  }

  toggle(): void {
    const next: Theme = this.theme() === 'dark' ? 'light' : 'dark';
    this.theme.set(next);
    this.apply(next);
    try {
      localStorage.setItem(STORAGE_KEY, next);
    } catch {
      /* armazenamento indisponível */
    }
  }

  private apply(theme: Theme): void {
    this.document.documentElement.setAttribute('data-theme', theme);
  }

  private initial(): Theme {
    try {
      const saved = localStorage.getItem(STORAGE_KEY);
      if (saved === 'light' || saved === 'dark') return saved;
    } catch {
      /* ignora */
    }
    return this.document.defaultView?.matchMedia?.('(prefers-color-scheme: dark)').matches ? 'dark' : 'light';
  }
}
