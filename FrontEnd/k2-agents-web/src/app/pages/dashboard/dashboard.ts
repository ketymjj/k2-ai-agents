import { Component, OnInit, computed, inject, signal } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { FormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatMenuModule } from '@angular/material/menu';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { MatSelectModule } from '@angular/material/select';
import { MatTooltipModule } from '@angular/material/tooltip';
import { TranslocoPipe, TranslocoService } from '@jsverse/transloco';
import { ChartConfiguration } from 'chart.js';
import { AgentsApi } from '../../core/api/agents.api';
import { CompaniesApi } from '../../core/api/companies.api';
import { Dashboard, ExportType, ReportFilter, ReportsApi } from '../../core/api/reports.api';
import { AuthService } from '../../core/auth/auth.service';
import { LanguageService } from '../../core/language.service';
import { AgentListItem, ChannelType, Company } from '../../core/models';
import { NotifyService } from '../../core/notify.service';
import { CATEGORICAL, CHART_COLORS, ChartView } from '../../shared/chart/chart';
import { localDate } from '../appointments/appointment-dialog/appointment-dialog';
import { ModuleGuide } from '../../shared/module-guide/module-guide';
import { Term } from '../../shared/term/term';

type Preset = '7' | '30' | '90' | 'custom';

interface KpiCard {
  key: string;
  icon: string;
  value: string;
  delta: number | null;
  /** Alta é boa (verde) ou ruim (vermelha)? Nulo = neutro. */
  upIsGood: boolean | null;
  hint?: string;
}

@Component({
  selector: 'app-dashboard',
  imports: [ModuleGuide, Term, FormsModule, MatButtonModule, MatFormFieldModule, MatIconModule, MatInputModule, MatMenuModule, MatProgressBarModule, MatSelectModule,
    MatTooltipModule, TranslocoPipe, ChartView],
  templateUrl: './dashboard.html',
  styleUrl: './dashboard.scss'
})
export class DashboardPage implements OnInit {
  private readonly api = inject(ReportsApi);
  private readonly agentsApi = inject(AgentsApi);
  private readonly companiesApi = inject(CompaniesApi);
  private readonly notify = inject(NotifyService);
  private readonly transloco = inject(TranslocoService);
  private readonly language = inject(LanguageService);
  protected readonly auth = inject(AuthService);

  protected readonly channels: ChannelType[] = ['WebChat', 'WhatsApp', 'Playground'];
  protected readonly exportTypes: ExportType[] = ['Conversations', 'Leads', 'Appointments', 'AIUsage'];
  /** Indicadores com explicação no glossário (Modo Iniciante). */
  protected readonly termFor: Record<string, string> = {
    resolutionRate: 'resolutionRate', transferred: 'handoff', newLeads: 'lead', avgResponse: 'latency', aiCost: 'estimatedCost', simulated: 'simulatedMode'
  };
  protected readonly canExport = computed(() => this.auth.hasRole('SuperAdmin', 'CompanyAdmin', 'AgentManager'));

  protected readonly preset = signal<Preset>('30');
  protected readonly from = signal(this.daysAgo(29));
  protected readonly to = signal(localDate(new Date()));
  protected readonly companyId = signal('');
  protected readonly agentId = signal('');
  protected readonly channel = signal<ChannelType | ''>('');

  protected readonly companies = signal<Company[]>([]);
  protected readonly agents = signal<AgentListItem[]>([]);
  protected readonly data = signal<Dashboard | null>(null);
  protected readonly loading = signal(false);
  protected readonly downloading = signal(false);

  private readonly locale = this.language.locale;
  /** Idioma já carregado e ativo: os textos dos gráficos recalculam quando ele muda. */
  private readonly activeLang = toSignal(this.transloco.langChanges$, { initialValue: this.transloco.getActiveLang() });
  private readonly t = (key: string, params?: Record<string, unknown>) => {
    this.activeLang();
    return this.transloco.translate(key, params);
  };

  protected readonly cards = computed<KpiCard[]>(() => {
    const d = this.data();
    if (!d) return [];
    const k = d.kpis;
    const p = d.previous;
    const n = (v: number) => new Intl.NumberFormat(this.locale()).format(v);
    const usd = (v: number) => 'US$ ' + new Intl.NumberFormat(this.locale(), { minimumFractionDigits: 2, maximumFractionDigits: 4 }).format(v);
    const delta = (a: number, b: number) => (b === 0 ? null : Math.round(((a - b) / b) * 1000) / 10);
    return [
      { key: 'conversations', icon: 'forum', value: n(k.conversations), delta: delta(k.conversations, p.conversations), upIsGood: null },
      {
        key: 'resolutionRate', icon: 'smart_toy', value: k.resolutionRate === null ? '—' : `${n(k.resolutionRate)}%`,
        delta: k.resolutionRate !== null && p.resolutionRate !== null ? Math.round((k.resolutionRate - p.resolutionRate) * 10) / 10 : null,
        upIsGood: true, hint: this.t('dashboard.resolutionHint', { resolved: k.resolvedByAI, closed: k.closedConversations })
      },
      { key: 'transferred', icon: 'support_agent', value: n(k.transferred), delta: delta(k.transferred, p.transferred), upIsGood: null },
      { key: 'newLeads', icon: 'person_add', value: n(k.newLeads), delta: delta(k.newLeads, p.newLeads), upIsGood: true,
        hint: this.t('dashboard.contactsHint', { count: k.newContacts }) },
      { key: 'appointments', icon: 'event_available', value: n(k.appointments), delta: delta(k.appointments, p.appointments), upIsGood: true,
        hint: this.t('dashboard.byAgentHint', { count: k.appointmentsByAgent }) },
      { key: 'avgResponse', icon: 'speed', value: k.avgAIResponseMs === null ? '—' : `${n(Math.round(k.avgAIResponseMs / 100) / 10)} s`,
        delta: k.avgAIResponseMs !== null && p.avgAIResponseMs ? delta(k.avgAIResponseMs, p.avgAIResponseMs) : null, upIsGood: false,
        hint: k.avgAIResponseMs === null ? this.t('dashboard.noRealAI') : undefined },
      { key: 'aiCost', icon: 'payments', value: usd(k.estimatedCostUsd), delta: delta(k.estimatedCostUsd, p.estimatedCostUsd), upIsGood: null,
        hint: this.t('dashboard.costHint', { calls: n(k.aiCalls), tokens: n(k.inputTokens + k.outputTokens) }) },
      { key: 'simulated', icon: 'science', value: n(k.simulatedReplies), delta: null, upIsGood: null, hint: this.t('dashboard.simulatedHint') }
    ];
  });

  protected readonly hasData = computed(() => {
    const k = this.data()?.kpis;
    return !!k && (k.conversations > 0 || k.aiCalls > 0 || k.newContacts > 0 || k.appointments > 0);
  });

  private readonly dayLabels = computed(() =>
    (this.data()?.daily ?? []).map((p) => new Date(p.date + 'T12:00:00').toLocaleDateString(this.locale(), { day: '2-digit', month: '2-digit' }))
  );

  protected readonly dailyChart = computed<ChartConfiguration>(() => {
    const daily = this.data()?.daily ?? [];
    return {
      type: 'line',
      data: {
        labels: this.dayLabels(),
        datasets: [
          { label: this.t('dashboard.series.conversations'), data: daily.map((p) => p.conversations), borderColor: CHART_COLORS.primary,
            backgroundColor: 'rgba(124, 58, 237, 0.12)', fill: true, cubicInterpolationMode: 'monotone', pointRadius: 2 },
          { label: this.t('dashboard.series.leads'), data: daily.map((p) => p.leads), borderColor: CHART_COLORS.green, backgroundColor: CHART_COLORS.green,
            cubicInterpolationMode: 'monotone', pointRadius: 2 },
          { label: this.t('dashboard.series.appointments'), data: daily.map((p) => p.appointments), borderColor: CHART_COLORS.amber,
            backgroundColor: CHART_COLORS.amber, cubicInterpolationMode: 'monotone', pointRadius: 2 }
        ]
      },
      options: { interaction: { mode: 'index', intersect: false }, scales: { y: { beginAtZero: true, ticks: { precision: 0 } }, x: { grid: { display: false } } } }
    };
  });

  protected readonly costChart = computed<ChartConfiguration>(() => {
    const daily = this.data()?.daily ?? [];
    return {
      type: 'bar',
      data: {
        labels: this.dayLabels(),
        datasets: [{ label: this.t('dashboard.series.cost'), data: daily.map((p) => p.estimatedCostUsd), backgroundColor: CHART_COLORS.primary, borderRadius: 4 }]
      },
      options: {
        plugins: { legend: { display: false }, tooltip: { callbacks: { label: (c) => `US$ ${Number(c.raw).toFixed(4)}` } } },
        scales: { y: { beginAtZero: true }, x: { grid: { display: false } } }
      }
    };
  });

  protected readonly channelChart = computed<ChartConfiguration>(() => {
    const rows = this.data()?.byChannel ?? [];
    return {
      type: 'doughnut',
      data: {
        labels: rows.map((r) => this.t('channelTypes.' + r.channel)),
        datasets: [{ data: rows.map((r) => r.conversations), backgroundColor: CATEGORICAL, borderWidth: 0 }]
      },
      options: { cutout: '62%', plugins: { legend: { position: 'bottom' } } }
    };
  });

  protected readonly statusChart = computed<ChartConfiguration>(() => {
    const rows = (this.data()?.conversationsByStatus ?? []).filter((r) => r.count > 0);
    const color: Record<string, string> = { Open: CHART_COLORS.blue, WaitingHuman: CHART_COLORS.amber, HumanActive: CHART_COLORS.primary, Closed: CHART_COLORS.gray };
    return {
      type: 'doughnut',
      data: {
        labels: rows.map((r) => this.t('conversationStatus.' + r.status)),
        datasets: [{ data: rows.map((r) => r.count), backgroundColor: rows.map((r) => color[r.status] ?? CHART_COLORS.gray), borderWidth: 0 }]
      },
      options: { cutout: '62%', plugins: { legend: { position: 'bottom' } } }
    };
  });

  protected readonly appointmentsChart = computed<ChartConfiguration>(() => {
    const rows = this.data()?.appointmentsByStatus ?? [];
    const color: Record<string, string> = { Scheduled: CHART_COLORS.blue, Confirmed: CHART_COLORS.green, Completed: CHART_COLORS.gray, NoShow: CHART_COLORS.amber, Cancelled: CHART_COLORS.red };
    return {
      type: 'bar',
      data: {
        labels: rows.map((r) => this.t('appointmentStatus.' + r.status)),
        datasets: [{ data: rows.map((r) => r.count), backgroundColor: rows.map((r) => color[r.status]), borderRadius: 4 }]
      },
      options: { indexAxis: 'y', plugins: { legend: { display: false } }, scales: { x: { beginAtZero: true, ticks: { precision: 0 } }, y: { grid: { display: false } } } }
    };
  });

  ngOnInit(): void {
    if (this.auth.isSuperAdmin()) {
      this.companiesApi.list({ status: 'Active', pageSize: 100 }).subscribe({ next: (r) => this.companies.set(r.items), error: () => undefined });
    }
    this.loadAgents();
    this.reload();
  }

  setPreset(preset: Preset): void {
    this.preset.set(preset);
    if (preset !== 'custom') {
      this.from.set(this.daysAgo(Number(preset) - 1));
      this.to.set(localDate(new Date()));
      this.reload();
    }
  }

  setRange(from: string, to: string): void {
    this.from.set(from);
    this.to.set(to);
    if (from && to && from <= to) this.reload();
  }

  selectCompany(id: string): void {
    this.companyId.set(id);
    this.agentId.set('');
    this.loadAgents();
    this.reload();
  }

  reload(): void {
    this.loading.set(true);
    this.api.dashboard(this.filter()).subscribe({
      next: (d) => {
        this.data.set(d);
        this.loading.set(false);
      },
      error: (err) => {
        this.loading.set(false);
        this.notify.error(err);
      }
    });
  }

  downloadPdf(): void {
    this.run(this.api.summaryPdf(this.filter()));
  }

  downloadCsv(type: ExportType): void {
    this.run(this.api.exportCsv(type, this.filter()));
  }

  protected formatMs(ms: number | null): string {
    return ms === null ? '—' : `${new Intl.NumberFormat(this.locale()).format(Math.round(ms / 100) / 10)} s`;
  }

  protected formatUsd(v: number): string {
    return 'US$ ' + new Intl.NumberFormat(this.locale(), { minimumFractionDigits: 2, maximumFractionDigits: 4 }).format(v);
  }

  private run(request: ReturnType<ReportsApi['summaryPdf']>): void {
    this.downloading.set(true);
    request.subscribe({
      next: () => this.downloading.set(false),
      error: (err) => {
        this.downloading.set(false);
        this.notify.error(err);
      }
    });
  }

  private filter(): ReportFilter {
    return {
      companyId: this.companyId() || undefined,
      from: this.from(),
      to: this.to(),
      agentId: this.agentId() || undefined,
      channel: this.channel(),
      lang: this.language.current()
    };
  }

  private loadAgents(): void {
    this.agentsApi.list({ companyId: this.companyId() || undefined, pageSize: 100 }).subscribe({
      next: (r) => this.agents.set(r.items),
      error: () => this.agents.set([])
    });
  }

  private daysAgo(days: number): string {
    const d = new Date();
    d.setDate(d.getDate() - days);
    return localDate(d);
  }
}
