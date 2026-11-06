import { DOCUMENT } from '@angular/common';
import { Injectable, computed, inject, signal } from '@angular/core';
import { TranslocoService } from '@jsverse/transloco';
import { firstValueFrom } from 'rxjs';
import { Language } from './models';

const STORAGE_KEY = 'k2.lang';

@Injectable({ providedIn: 'root' })
export class LanguageService {
  private readonly transloco = inject(TranslocoService);
  private readonly document = inject(DOCUMENT);

  readonly current = signal<Language>(this.read() ?? 'pt-BR');
  /** Localidade para os pipes de número e data. */
  readonly locale = computed(() => (this.current() === 'pt-BR' ? 'pt-BR' : 'en-US'));

  constructor() {
    this.apply(this.current());
  }

  hasLocalChoice(): boolean {
    return this.read() !== null;
  }

  /** Resolve quando as traduções do idioma atual estiverem carregadas. */
  ready(): Promise<unknown> {
    return firstValueFrom(this.transloco.load(this.current()));
  }

  use(lang: Language): void {
    this.current.set(lang);
    this.apply(lang);
    try {
      localStorage.setItem(STORAGE_KEY, lang);
    } catch {
      /* armazenamento indisponível: preferência vale só nesta sessão */
    }
  }

  /** Carrega o arquivo antes de ativar o idioma, para que a tela troque de uma vez, sem rótulos vazios. */
  private apply(lang: Language): void {
    this.document.documentElement.lang = lang;
    this.transloco.load(lang).subscribe({
      next: () => this.transloco.setActiveLang(lang),
      error: () => this.transloco.setActiveLang(lang)
    });
  }

  private read(): Language | null {
    try {
      const value = localStorage.getItem(STORAGE_KEY);
      return value === 'pt-BR' || value === 'en' ? value : null;
    } catch {
      return null;
    }
  }
}
