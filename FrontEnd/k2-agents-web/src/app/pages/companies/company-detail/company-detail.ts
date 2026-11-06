import { DatePipe } from '@angular/common';
import { Component, computed, effect, inject, input, signal } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatDialog } from '@angular/material/dialog';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { RouterLink } from '@angular/router';
import { TranslocoPipe } from '@jsverse/transloco';
import { filter, forkJoin } from 'rxjs';
import { CompaniesApi } from '../../../core/api/companies.api';
import { AuthService } from '../../../core/auth/auth.service';
import { Company, CompanySummary } from '../../../core/models';
import { NotifyService } from '../../../core/notify.service';
import { DocumentPipe } from '../../../shared/document.pipe';
import { CompanyDialog } from '../company-dialog/company-dialog';
import { ModuleGuide } from '../../../shared/module-guide/module-guide';

interface Indicator {
  icon: string;
  labelKey: string;
  value: number;
  detailKey?: string;
  detailCount?: number;
  tone?: 'warning';
  link?: unknown[];
  queryParams?: Record<string, string>;
}

@Component({
  selector: 'app-company-detail',
  imports: [ModuleGuide, DatePipe, RouterLink, MatButtonModule, MatIconModule, MatProgressBarModule, TranslocoPipe, DocumentPipe],
  templateUrl: './company-detail.html',
  styleUrl: './company-detail.scss'
})
export class CompanyDetail {
  private readonly api = inject(CompaniesApi);
  private readonly dialog = inject(MatDialog);
  private readonly notify = inject(NotifyService);
  protected readonly auth = inject(AuthService);

  /** Parâmetro de rota :id. */
  readonly id = input.required<string>();

  protected readonly company = signal<Company | null>(null);
  protected readonly summary = signal<CompanySummary | null>(null);
  protected readonly loading = signal(true);
  protected readonly notFound = signal(false);

  protected readonly canEdit = computed(() => this.auth.hasRole('SuperAdmin', 'CompanyAdmin'));
  protected readonly canManageUsers = computed(() => this.auth.hasRole('SuperAdmin', 'CompanyAdmin'));

  protected readonly indicators = computed<Indicator[]>(() => {
    const s = this.summary();
    if (!s) return [];
    return [
      {
        icon: 'smart_toy', labelKey: 'companies.summary.agents', value: s.agents,
        detailKey: 'companies.summary.activeAgents', detailCount: s.activeAgents,
        link: ['/agents'], queryParams: this.auth.isSuperAdmin() ? { companyId: s.companyId } : ({} as Record<string, string>)
      },
      {
        icon: 'forum', labelKey: 'companies.summary.conversations', value: s.conversations,
        detailKey: 'companies.summary.openConversations', detailCount: s.openConversations, link: ['/conversations']
      },
      { icon: 'support_agent', labelKey: 'companies.summary.waitingHuman', value: s.waitingHumanConversations, tone: s.waitingHumanConversations > 0 ? 'warning' : undefined },
      {
        icon: 'contacts', labelKey: 'companies.summary.contacts', value: s.contacts,
        detailKey: 'companies.summary.leads', detailCount: s.leads, link: ['/contacts']
      },
      { icon: 'event', labelKey: 'companies.summary.appointments', value: s.appointments },
      {
        icon: 'menu_book', labelKey: 'companies.summary.knowledge', value: s.knowledgeDocuments,
        link: ['/knowledge'], queryParams: this.auth.isSuperAdmin() ? { companyId: s.companyId } : ({} as Record<string, string>)
      },
      { icon: 'group', labelKey: 'companies.summary.users', value: s.users }
    ];
  });

  constructor() {
    effect(() => this.load(this.id()));
  }

  load(id: string): void {
    this.loading.set(true);
    this.notFound.set(false);
    forkJoin({ company: this.api.get(id), summary: this.api.summary(id) }).subscribe({
      next: ({ company, summary }) => {
        this.company.set(company);
        this.summary.set(summary);
        this.loading.set(false);
      },
      error: (err) => {
        this.loading.set(false);
        if (err?.status === 404) this.notFound.set(true);
        else this.notify.error(err);
      }
    });
  }

  edit(): void {
    this.dialog
      .open(CompanyDialog, { data: this.company(), width: '640px', maxWidth: '95vw' })
      .afterClosed()
      .pipe(filter(Boolean))
      .subscribe((saved: Company) => {
        this.company.set(saved);
        this.notify.success('companies.updated');
      });
  }
}
