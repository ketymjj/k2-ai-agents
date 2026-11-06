import { Component, OnInit, computed, inject, input, signal } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { FormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { MatSelectModule } from '@angular/material/select';
import { MatTabsModule } from '@angular/material/tabs';
import { Router, RouterLink } from '@angular/router';
import { TranslocoPipe, TranslocoService } from '@jsverse/transloco';
import { catchError, forkJoin, map, of } from 'rxjs';
import { AgentsApi } from '../../core/api/agents.api';
import { ChannelsApi } from '../../core/api/channels.api';
import { KnowledgeApi } from '../../core/api/knowledge.api';
import { ReportsApi } from '../../core/api/reports.api';
import { AuthService } from '../../core/auth/auth.service';
import { LanguageService } from '../../core/language.service';
import { LearningService } from '../../core/learning.service';
import { AIModelOption, BUSINESS_TYPES, BusinessType, Role } from '../../core/models';
import { NotifyService } from '../../core/notify.service';
import { localDate } from '../appointments/appointment-dialog/appointment-dialog';

const TABS = ['tutorial', 'glossary', 'prompts', 'demo', 'costs'] as const;
type Tab = (typeof TABS)[number];

export const GLOSSARY_TERMS = [
  'ai', 'model', 'prompt', 'systemPrompt', 'token', 'temperature', 'rag', 'knowledgeBase', 'chunk', 'faq',
  'hallucination', 'promptInjection', 'handoff', 'lead', 'simulatedMode', 'estimatedCost', 'latency', 'resolutionRate'
];

const MANAGERS: Role[] = ['SuperAdmin', 'CompanyAdmin', 'AgentManager'];

interface TutorialStep {
  key: string;
  icon: string;
  link: string[];
  done: boolean | null;
  roles?: Role[];
}

interface DemoMessage {
  from: 'customer' | 'agent' | 'human' | 'system';
  text: string;
  note?: string;
}

@Component({
  selector: 'app-learn',
  imports: [FormsModule, RouterLink, MatButtonModule, MatFormFieldModule, MatIconModule, MatInputModule, MatProgressBarModule, MatSelectModule,
    MatTabsModule, TranslocoPipe],
  templateUrl: './learn.html',
  styleUrl: './learn.scss'
})
export class LearnPage implements OnInit {
  /** ?tab=glossary (links dos guias de cada módulo). */
  readonly tab = input<string>();

  private readonly agentsApi = inject(AgentsApi);
  private readonly knowledgeApi = inject(KnowledgeApi);
  private readonly channelsApi = inject(ChannelsApi);
  private readonly reportsApi = inject(ReportsApi);
  private readonly transloco = inject(TranslocoService);
  private readonly notify = inject(NotifyService);
  private readonly router = inject(Router);
  private readonly locale = inject(LanguageService).locale;
  protected readonly auth = inject(AuthService);
  protected readonly learning = inject(LearningService);

  protected readonly tabs = TABS;
  protected readonly businessTypes = BUSINESS_TYPES;
  protected readonly selectedIndex = computed(() => Math.max(0, TABS.indexOf((this.tab() ?? 'tutorial') as Tab)));
  private readonly lang = toSignal(this.transloco.langChanges$, { initialValue: this.transloco.getActiveLang() });

  // ---------- Tutorial com progresso real ----------
  protected readonly progress = signal<Record<string, boolean | null>>({});
  protected readonly loadingProgress = signal(true);
  protected readonly isManager = computed(() => this.auth.hasRole(...MANAGERS));

  protected readonly steps = computed<TutorialStep[]>(() => {
    const p = this.progress();
    return [
      { key: 'createAgent', icon: 'smart_toy', link: ['/agents/new'], done: p['createAgent'] ?? null, roles: MANAGERS },
      { key: 'knowledge', icon: 'menu_book', link: ['/knowledge'], done: p['knowledge'] ?? null, roles: MANAGERS },
      { key: 'playground', icon: 'science', link: ['/playground'], done: p['playground'] ?? null, roles: MANAGERS },
      { key: 'activate', icon: 'toggle_on', link: ['/agents'], done: p['activate'] ?? null, roles: MANAGERS },
      { key: 'webchat', icon: 'hub', link: ['/channels'], done: p['webchat'] ?? null, roles: MANAGERS },
      { key: 'conversation', icon: 'forum', link: ['/conversations'], done: p['conversation'] ?? null },
      { key: 'reports', icon: 'space_dashboard', link: ['/dashboard'], done: null }
    ];
  });
  protected readonly completed = computed(() => this.steps().filter((s) => s.done).length);
  protected readonly trackable = computed(() => this.steps().filter((s) => s.done !== null).length);

  // ---------- Glossário ----------
  protected readonly search = signal('');
  protected readonly glossary = computed(() => {
    this.lang();
    const term = this.normalize(this.search());
    return GLOSSARY_TERMS.map((key) => ({
      key,
      term: this.transloco.translate(`learn.glossary.${key}.term`),
      definition: this.transloco.translate(`learn.glossary.${key}.definition`),
      example: this.transloco.translate(`learn.glossary.${key}.example`)
    }))
      .filter((g) => !term || this.normalize(`${g.term} ${g.definition}`).includes(term))
      .sort((a, b) => a.term.localeCompare(b.term, this.locale()));
  });

  // ---------- Conversa de demonstração ----------
  protected readonly demo = computed<DemoMessage[]>(() => {
    this.lang();
    const raw = this.transloco.translateObject<DemoMessage[]>('learn.demo.messages');
    return Array.isArray(raw) ? raw : [];
  });

  // ---------- Tokens e custos ----------
  protected readonly models = signal<AIModelOption[]>([]);
  protected readonly modelId = signal('');
  protected readonly conversationsPerMonth = signal(500);
  protected readonly repliesPerConversation = signal(5);
  protected readonly wordsPerReply = signal(60);
  protected readonly promptTokens = signal(900);

  /** Estimativa transparente: cada resposta envia o prompt + histórico e recebe a resposta; ~1,4 token por palavra em português. */
  protected readonly estimate = computed(() => {
    const model = this.models().find((m) => m.id === this.modelId());
    const replies = Math.max(0, this.conversationsPerMonth()) * Math.max(0, this.repliesPerConversation());
    const outputPerReply = Math.round(Math.max(0, this.wordsPerReply()) * 1.4);
    // Histórico médio: metade da conversa, com mensagens do cliente (~20 palavras) e da IA.
    const historyPerReply = Math.round(((this.repliesPerConversation() - 1) / 2) * (outputPerReply + 28));
    const inputPerReply = Math.max(0, this.promptTokens()) + historyPerReply + 28;
    const inputTokens = replies * inputPerReply;
    const outputTokens = replies * outputPerReply;
    const cost = model ? (inputTokens * model.inputPricePer1M + outputTokens * model.outputPricePer1M) / 1_000_000 : 0;
    return { replies, inputPerReply, outputPerReply, inputTokens, outputTokens, cost, model };
  });

  ngOnInit(): void {
    this.loadProgress();
    this.agentsApi.models().subscribe({
      next: (models) => {
        this.models.set(models);
        this.modelId.set(models.find((m) => m.isDefault)?.id ?? models[0]?.id ?? '');
      },
      error: () => this.models.set([])
    });
  }

  selectTab(index: number): void {
    this.router.navigate([], { queryParams: { tab: TABS[index] }, replaceUrl: true });
  }

  canDo(step: TutorialStep): boolean {
    return !step.roles || this.auth.hasRole(...step.roles);
  }

  copy(text: string): void {
    navigator.clipboard?.writeText(text).then(
      () => this.notify.success('learn.copied'),
      () => this.notify.error(null)
    );
  }

  protected promptExample(type: BusinessType): string {
    this.lang();
    return this.transloco.translate(`learn.prompts.examples.${type}`);
  }

  protected n(value: number, digits = 0): string {
    return new Intl.NumberFormat(this.locale(), { maximumFractionDigits: digits, minimumFractionDigits: digits }).format(value);
  }

  /** Lê o estado real da conta: cada passo é marcado só quando o dado existe de fato. */
  private loadProgress(): void {
    const safe = <T>(o: import('rxjs').Observable<T>) => o.pipe(catchError(() => of(null)));
    const from = new Date();
    from.setDate(from.getDate() - 89);
    const period = { from: localDate(from), to: localDate(new Date()) };
    forkJoin({
      agents: safe(this.agentsApi.list({ pageSize: 100 })),
      knowledge: safe(this.knowledgeApi.list({ pageSize: 1 })),
      channels: safe(this.channelsApi.list()),
      playground: safe(this.reportsApi.dashboard({ ...period, channel: 'Playground' }).pipe(map((d) => d.kpis.conversations))),
      customers: safe(this.reportsApi.dashboard(period).pipe(map((d) => d.kpis.conversations - d.demoConversations)))
    }).subscribe(({ agents, knowledge, channels, playground, customers }) => {
      this.progress.set({
        createAgent: agents ? agents.total > 0 : null,
        activate: agents ? agents.items.some((a) => a.status === 'Active') : null,
        knowledge: knowledge ? knowledge.total > 0 : null,
        playground: playground === null ? null : playground > 0,
        webchat: channels ? channels.some((c) => c.channelType === 'WebChat' && c.status === 'Active') : null,
        conversation: customers === null ? null : customers > 0
      });
      this.loadingProgress.set(false);
    });
  }

  private normalize(text: string): string {
    return text.toLowerCase().normalize('NFD').replace(/[̀-ͯ]/g, '');
  }
}
