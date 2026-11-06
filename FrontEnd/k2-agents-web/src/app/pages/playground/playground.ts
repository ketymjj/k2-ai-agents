import { DatePipe, DecimalPipe } from '@angular/common';
import { Component, ElementRef, OnInit, afterRenderEffect, computed, inject, input, signal, viewChild } from '@angular/core';
import { FormControl, FormsModule, ReactiveFormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { MatSelectModule } from '@angular/material/select';
import { MatSlideToggleModule } from '@angular/material/slide-toggle';
import { MatTooltipModule } from '@angular/material/tooltip';
import { Router, RouterLink } from '@angular/router';
import { TranslocoPipe } from '@jsverse/transloco';
import { forkJoin } from 'rxjs';
import { AgentsApi } from '../../core/api/agents.api';
import { PlaygroundApi } from '../../core/api/playground.api';
import { errorMessage } from '../../core/http-error';
import { LanguageService } from '../../core/language.service';
import {
  Agent,
  AgentListItem,
  ChatMessage,
  PlaygroundSession,
  PlaygroundSessionSummary,
  PlaygroundStatus,
  SessionTotals
} from '../../core/models';
import { NotifyService } from '../../core/notify.service';
import { ModuleGuide } from '../../shared/module-guide/module-guide';
import { Term } from '../../shared/term/term';

interface FailedSend {
  content: string;
  message: string;
}

const EMPTY_TOTALS: SessionTotals = { requests: 0, inputTokens: 0, outputTokens: 0, estimatedCostUsd: 0, simulatedReplies: 0 };

@Component({
  selector: 'app-playground',
  imports: [ModuleGuide, Term, 
    DatePipe, DecimalPipe, RouterLink, FormsModule, ReactiveFormsModule, MatButtonModule, MatIconModule, MatFormFieldModule,
    MatSelectModule, MatSlideToggleModule, MatProgressBarModule, MatTooltipModule, TranslocoPipe
  ],
  templateUrl: './playground.html',
  styleUrl: './playground.scss'
})
export class Playground implements OnInit {
  private readonly agentsApi = inject(AgentsApi);
  private readonly api = inject(PlaygroundApi);
  private readonly notify = inject(NotifyService);
  private readonly router = inject(Router);
  protected readonly locale = inject(LanguageService).locale;

  /** Query param opcional: agente pré-selecionado. */
  readonly agentId = input<string>();

  private readonly scroller = viewChild<ElementRef<HTMLElement>>('scroller');
  private readonly inputEl = viewChild<ElementRef<HTMLTextAreaElement>>('messageInput');

  protected readonly agents = signal<AgentListItem[]>([]);
  protected readonly agent = signal<Agent | null>(null);
  protected readonly status = signal<PlaygroundStatus | null>(null);
  protected readonly session = signal<PlaygroundSession | null>(null);
  protected readonly messages = signal<ChatMessage[]>([]);
  protected readonly totals = signal<SessionTotals>(EMPTY_TOTALS);
  protected readonly history = signal<PlaygroundSessionSummary[]>([]);
  protected readonly loading = signal(true);
  protected readonly sending = signal(false);
  protected readonly pendingText = signal<string | null>(null);
  protected readonly failed = signal<FailedSend | null>(null);
  protected readonly simulate = signal(false);
  /** Respostas em que o agente pediu transferência (no Playground a conversa não muda de status). */
  protected readonly handoffReplies = signal<Set<string>>(new Set());

  protected readonly agentControl = new FormControl<string>('', { nonNullable: true });
  protected readonly draft = signal('');

  protected readonly aiConfigured = computed(() => this.status()?.aiConfigured ?? false);
  /** Sem chave, o único modo possível é o demonstrativo. */
  protected readonly simulatedMode = computed(() => !this.aiConfigured() || this.simulate());
  protected readonly maxChars = computed(() => this.status()?.maxMessageChars ?? 2000);
  protected readonly canSend = computed(
    () => !!this.session() && !this.sending() && this.draft().trim().length > 0 && this.draft().length <= this.maxChars()
  );

  constructor() {
    // Mantém a conversa rolada até a última mensagem.
    afterRenderEffect(() => {
      this.messages();
      this.pendingText();
      this.sending();
      const el = this.scroller()?.nativeElement;
      if (el) el.scrollTop = el.scrollHeight;
    });
  }

  ngOnInit(): void {
    this.agentsApi.list({ pageSize: 100 }).subscribe({
      next: (result) => {
        const usable = result.items.filter((a) => a.status !== 'Inactive');
        this.agents.set(usable);
        const initial = usable.find((a) => a.id === this.agentId()) ?? usable[0];
        this.loading.set(false);
        if (initial) {
          this.agentControl.setValue(initial.id, { emitEvent: false });
          this.selectAgent(initial.id);
        }
      },
      error: (err) => {
        this.loading.set(false);
        this.notify.error(err);
      }
    });
    this.agentControl.valueChanges.subscribe((id) => this.selectAgent(id));
  }

  selectAgent(id: string): void {
    this.router.navigate([], { queryParams: { agentId: id }, replaceUrl: true });
    this.failed.set(null);
    forkJoin({ agent: this.agentsApi.get(id), status: this.api.status(id), session: this.api.start(id) }).subscribe({
      next: ({ agent, status, session }) => {
        this.agent.set(agent);
        this.status.set(status);
        this.applySession(session);
        this.loadHistory(id);
        this.focusInput();
      },
      error: (err) => this.notify.error(err)
    });
  }

  send(content = this.draft()): void {
    const session = this.session();
    const text = content.trim();
    if (!session || !text || this.sending()) return;

    this.sending.set(true);
    this.failed.set(null);
    this.pendingText.set(text);
    this.draft.set('');

    this.api.send(session.id, text, this.simulatedMode()).subscribe({
      next: (result) => {
        this.messages.update((list) => [...list, result.userMessage, result.reply]);
        if (result.handoffRequested) this.handoffReplies.update((s) => new Set(s).add(result.reply.id));
        this.totals.set(result.totals);
        this.pendingText.set(null);
        this.sending.set(false);
        if (!result.reply.isSimulated) this.refreshStatus();
        this.focusInput();
      },
      error: (err) => {
        this.pendingText.set(null);
        this.sending.set(false);
        this.failed.set({ content: text, message: errorMessage(err) });
      }
    });
  }

  retry(): void {
    const failed = this.failed();
    if (failed) this.send(failed.content);
  }

  editFailed(): void {
    const failed = this.failed();
    if (!failed) return;
    this.draft.set(failed.content);
    this.failed.set(null);
    this.focusInput();
  }

  onKeydown(event: KeyboardEvent): void {
    if (event.key === 'Enter' && !event.shiftKey && !event.isComposing) {
      event.preventDefault();
      if (this.canSend()) this.send();
    }
  }

  /** Encerra a sessão atual e começa uma nova conversa do zero. */
  clear(): void {
    const session = this.session();
    const agentId = this.agentControl.value;
    if (!session || !agentId) return;
    this.api.close(session.id).subscribe({
      next: () =>
        this.api.start(agentId).subscribe({
          next: (fresh) => {
            this.applySession(fresh);
            this.loadHistory(agentId);
            this.focusInput();
          },
          error: (err) => this.notify.error(err)
        }),
      error: (err) => this.notify.error(err)
    });
  }

  openSession(summary: PlaygroundSessionSummary): void {
    if (summary.id === this.session()?.id) return;
    this.api.get(summary.id).subscribe({
      next: (session) => this.applySession(session),
      error: (err) => this.notify.error(err)
    });
  }

  private applySession(session: PlaygroundSession): void {
    this.session.set(session);
    this.messages.set(session.messages);
    this.totals.set(session.totals);
    this.failed.set(null);
  }

  private loadHistory(agentId: string): void {
    this.api.sessions(agentId).subscribe({ next: (list) => this.history.set(list), error: () => this.history.set([]) });
  }

  private refreshStatus(): void {
    const id = this.agentControl.value;
    if (id) this.api.status(id).subscribe({ next: (s) => this.status.set(s), error: () => undefined });
  }

  private focusInput(): void {
    setTimeout(() => this.inputEl()?.nativeElement.focus());
  }
}
