import { Component, computed, inject, input } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { MatIconModule } from '@angular/material/icon';
import { MatTooltipModule } from '@angular/material/tooltip';
import { TranslocoService } from '@jsverse/transloco';
import { LearningService } from '../../core/learning.service';

/**
 * Termo técnico com definição do glossário. Com o Modo Iniciante desligado, renderiza só o texto,
 * sem nenhum efeito visual — a interface profissional não muda.
 */
@Component({
  selector: 'app-term',
  imports: [MatIconModule, MatTooltipModule],
  templateUrl: './term.html',
  styleUrl: './term.scss'
})
export class Term {
  readonly key = input.required<string>();

  private readonly learning = inject(LearningService);
  private readonly transloco = inject(TranslocoService);
  private readonly lang = toSignal(this.transloco.langChanges$, { initialValue: this.transloco.getActiveLang() });

  protected readonly active = computed(() => this.learning.enabled());
  protected readonly definition = computed(() => {
    this.lang();
    return this.active() ? this.transloco.translate(`learn.glossary.${this.key()}.definition`) : '';
  });
}
