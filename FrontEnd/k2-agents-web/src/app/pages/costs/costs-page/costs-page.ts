import { Component, OnInit, computed, inject, signal } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { FormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatDialog } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { MatSelectModule } from '@angular/material/select';
import { MatTooltipModule } from '@angular/material/tooltip';
import { TranslocoPipe, TranslocoService } from '@jsverse/transloco';
import { ChartConfiguration } from 'chart.js';
import { filter } from 'rxjs';
import { CompaniesApi } from '../../../core/api/companies.api';
import { CostMonth, CostReport, ReportsApi } from '../../../core/api/reports.api';
import { AuthService } from '../../../core/auth/auth.service';
import { LanguageService } from '../../../core/language.service';
import { Company } from '../../../core/models';
import { NotifyService } from '../../../core/notify.service';
import { CHART_COLORS, ChartView } from '../../../shared/chart/chart';
import { confirmAction } from '../../../shared/confirm-dialog/confirm-dialog';
import { BillingDialog, BillingDialogData } from '../billing-dialog/billing-dialog';
import { ModuleGuide } from '../../../shared/module-guide/module-guide';

function monthOf(d: Date): string {
  return `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}`;
}

@Component({
  selector: 'app-costs-page',
  imports: [ModuleGuide, FormsModule, MatButtonModule, MatFormFieldModule, MatIconModule, MatInputModule, MatProgressBarModule, MatSelectModule, MatTooltipModule,
    TranslocoPipe, ChartView],
  templateUrl: './costs-page.html',
  styleUrl: './costs-page.scss'
})
export class CostsPage implements OnInit {
  private readonly api = inject(ReportsApi);
  private readonly companiesApi = inject(CompaniesApi);
  private readonly dialog = inject(MatDialog);
  private readonly notify = inject(NotifyService);
  private readonly transloco = inject(TranslocoService);
  private readonly locale = inject(LanguageService).locale;
  protected readonly auth = inject(AuthService);

  protected readonly from = signal(monthOf(new Date(new Date().getFullYear(), new Date().getMonth() - 11, 1)));
  protected readonly to = signal(monthOf(new Date()));
  protected readonly companyId = signal('');
  protected readonly companies = signal<Company[]>([]);
  protected readonly report = signal<CostReport | null>(null);
  protected readonly loading = signal(false);

  private readonly activeLang = toSignal(this.transloco.langChanges$, { initialValue: this.transloco.getActiveLang() });

  protected readonly difference = computed(() => {
    const r = this.report();
    if (!r || r.totalBilledUsd === null) return null;
    // Compara só os meses com fatura lançada: meses sem fatura não entram na diferença.
    const billedMonths = r.months.filter((m) => m.billedUsd !== null);
    const estimated = billedMonths.reduce((s, m) => s + m.estimatedUsd, 0);
    return { usd: r.totalBilledUsd - estimated, pct: estimated > 0 ? ((r.totalBilledUsd - estimated) / estimated) * 100 : null };
  });

  protected readonly chart = computed<ChartConfiguration>(() => {
    this.activeLang();
    const months = this.report()?.months ?? [];
    const datasets: ChartConfiguration['data']['datasets'] = [
      { label: this.transloco.translate('costs.estimated'), data: months.map((m) => m.estimatedUsd), backgroundColor: CHART_COLORS.primary, borderRadius: 4 }
    ];
    if (this.report()?.canSeeBilling) {
      datasets.push({ label: this.transloco.translate('costs.billed'), data: months.map((m) => m.billedUsd), backgroundColor: CHART_COLORS.green, borderRadius: 4 });
    }
    return {
      type: 'bar',
      data: { labels: months.map((m) => this.monthLabel(m.month)), datasets },
      options: {
        plugins: { tooltip: { callbacks: { label: (c) => `${c.dataset.label}: ${c.raw === null ? '—' : this.usd(Number(c.raw))}` } } },
        scales: { y: { beginAtZero: true }, x: { grid: { display: false } } }
      }
    };
  });

  ngOnInit(): void {
    if (this.auth.isSuperAdmin()) {
      this.companiesApi.list({ status: 'Active', pageSize: 100 }).subscribe({ next: (r) => this.companies.set(r.items), error: () => undefined });
    }
    this.reload();
  }

  setRange(from: string, to: string): void {
    this.from.set(from);
    this.to.set(to);
    if (from && to && from <= to) this.reload();
  }

  reload(): void {
    this.loading.set(true);
    this.api.costs(this.companyId() || undefined, this.from(), this.to()).subscribe({
      next: (r) => {
        this.report.set(r);
        this.loading.set(false);
      },
      error: (err) => {
        this.loading.set(false);
        this.notify.error(err);
      }
    });
  }

  editBilling(month: CostMonth): void {
    this.dialog
      .open<BillingDialog, BillingDialogData, CostMonth>(BillingDialog, { data: { month }, width: '460px', maxWidth: '95vw' })
      .afterClosed()
      .pipe(filter(Boolean))
      .subscribe(() => {
        this.notify.success('costs.billingSaved');
        this.reload();
      });
  }

  removeBilling(month: CostMonth): void {
    confirmAction(this.dialog, {
      title: this.transloco.translate('costs.removeTitle'),
      message: this.transloco.translate('costs.removeConfirm', { month: this.monthLabel(month.month) }),
      confirmLabel: this.transloco.translate('common.delete'),
      danger: true
    })
      .pipe(filter(Boolean))
      .subscribe(() =>
        this.api.deleteBilling(month.month).subscribe({
          next: () => {
            this.notify.success('costs.billingRemoved');
            this.reload();
          },
          error: (err) => this.notify.error(err)
        })
      );
  }

  protected usd(v: number): string {
    return 'US$ ' + new Intl.NumberFormat(this.locale(), { minimumFractionDigits: 2, maximumFractionDigits: 4 }).format(v);
  }

  protected num(v: number): string {
    return new Intl.NumberFormat(this.locale()).format(v);
  }

  protected pct(v: number | null): string {
    return v === null ? '—' : `${v > 0 ? '+' : ''}${new Intl.NumberFormat(this.locale(), { maximumFractionDigits: 1 }).format(v)}%`;
  }

  protected monthLabel(month: string): string {
    const [y, m] = month.split('-').map(Number);
    return new Date(y, m - 1, 1).toLocaleDateString(this.locale(), { month: 'short', year: 'numeric' });
  }
}
