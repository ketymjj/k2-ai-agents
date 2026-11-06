import { Component, computed, inject, input, signal } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { RouterLink } from '@angular/router';
import { TranslocoPipe, TranslocoService } from '@jsverse/transloco';
import { LearningService } from '../../core/learning.service';

interface GuideContent {
  title: string;
  body: string;
  steps: string[];
}

const MINIMIZED_KEY = 'k2.learn.minimized';

/** Explicação do módulo exibida no topo da página quando o Modo Iniciante está ligado. */
@Component({
  selector: 'app-module-guide',
  imports: [MatButtonModule, MatIconModule, RouterLink, TranslocoPipe],
  templateUrl: './module-guide.html',
  styleUrl: './module-guide.scss'
})
export class ModuleGuide {
  readonly module = input.required<string>();
  /** Aba da central de aprendizado indicada no link "Saiba mais". */
  readonly learnTab = input<string>('tutorial');

  protected readonly learning = inject(LearningService);
  private readonly transloco = inject(TranslocoService);
  private readonly lang = toSignal(this.transloco.langChanges$, { initialValue: this.transloco.getActiveLang() });
  private readonly minimizedSet = signal<string[]>(this.readMinimized());

  protected readonly minimized = computed(() => this.minimizedSet().includes(this.module()));
  protected readonly content = computed<GuideContent>(() => {
    this.lang();
    const raw = this.transloco.translateObject<GuideContent>(`learn.modules.${this.module()}`) as Partial<GuideContent> | undefined;
    return { title: raw?.title ?? '', body: raw?.body ?? '', steps: Array.isArray(raw?.steps) ? raw.steps : [] };
  });

  toggleMinimized(): void {
    const list = this.minimizedSet();
    const next = this.minimized() ? list.filter((m) => m !== this.module()) : [...list, this.module()];
    this.minimizedSet.set(next);
    try {
      localStorage.setItem(MINIMIZED_KEY, JSON.stringify(next));
    } catch {
      /* armazenamento indisponível: vale só nesta sessão */
    }
  }

  private readMinimized(): string[] {
    try {
      const parsed = JSON.parse(localStorage.getItem(MINIMIZED_KEY) ?? '[]');
      return Array.isArray(parsed) ? parsed.filter((v): v is string => typeof v === 'string') : [];
    } catch {
      return [];
    }
  }
}
