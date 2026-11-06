import { DatePipe } from '@angular/common';
import { Component, DestroyRef, OnInit, computed, inject, input, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormControl, ReactiveFormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatMenuModule } from '@angular/material/menu';
import { MatPaginatorModule, PageEvent } from '@angular/material/paginator';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { MatSelectModule } from '@angular/material/select';
import { Router, RouterLink } from '@angular/router';
import { TranslocoPipe } from '@jsverse/transloco';
import { debounceTime, distinctUntilChanged, merge, of } from 'rxjs';
import { AgentsApi } from '../../../core/api/agents.api';
import { CompaniesApi } from '../../../core/api/companies.api';
import { AuthService } from '../../../core/auth/auth.service';
import { AGENT_MANAGER_ROLES, AgentListItem, AgentStatus, BUSINESS_TYPES, BusinessType, Company } from '../../../core/models';
import { NotifyService } from '../../../core/notify.service';
import { AgentActions } from '../agent-actions.service';
import { BUSINESS_TYPE_ICONS } from '../agent-ui';
import { ModuleGuide } from '../../../shared/module-guide/module-guide';

@Component({
  selector: 'app-agents-list',
  imports: [ModuleGuide, 
    DatePipe, RouterLink, ReactiveFormsModule, MatButtonModule, MatIconModule, MatFormFieldModule, MatInputModule,
    MatSelectModule, MatMenuModule, MatPaginatorModule, MatProgressBarModule, TranslocoPipe
  ],
  templateUrl: './agents-list.html',
  styleUrl: './agents-list.scss'
})
export class AgentsList implements OnInit {
  private readonly api = inject(AgentsApi);
  private readonly companiesApi = inject(CompaniesApi);
  private readonly actions = inject(AgentActions);
  private readonly notify = inject(NotifyService);
  private readonly router = inject(Router);
  private readonly destroyRef = inject(DestroyRef);
  protected readonly auth = inject(AuthService);

  /** Query param opcional (vindo do detalhe da empresa). */
  readonly companyId = input<string>();

  protected readonly businessTypes = BUSINESS_TYPES;
  protected readonly icons = BUSINESS_TYPE_ICONS;
  protected readonly canManage = computed(() => this.auth.hasRole(...AGENT_MANAGER_ROLES));

  protected readonly search = new FormControl('', { nonNullable: true });
  protected readonly status = new FormControl<AgentStatus | ''>('', { nonNullable: true });
  protected readonly type = new FormControl<BusinessType | ''>('', { nonNullable: true });
  protected readonly company = new FormControl('', { nonNullable: true });

  protected readonly agents = signal<AgentListItem[]>([]);
  protected readonly companies = signal<Company[]>([]);
  protected readonly total = signal(0);
  protected readonly page = signal(0);
  protected readonly pageSize = signal(12);
  protected readonly loading = signal(false);

  ngOnInit(): void {
    this.company.setValue(this.companyId() ?? '', { emitEvent: false });
    if (this.auth.isSuperAdmin()) {
      this.companiesApi.list({ pageSize: 100 }).subscribe({ next: (r) => this.companies.set(r.items), error: (e) => this.notify.error(e) });
    }
    merge(
      this.search.valueChanges.pipe(debounceTime(300), distinctUntilChanged()),
      this.status.valueChanges,
      this.type.valueChanges,
      this.company.valueChanges,
      of(null)
    )
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe((value) => this.reload(value !== null));
  }

  reload(resetPage = false): void {
    if (resetPage) this.page.set(0);
    this.loading.set(true);
    this.api
      .list({
        search: this.search.value,
        status: this.status.value,
        businessType: this.type.value,
        companyId: this.company.value || undefined,
        page: this.page() + 1,
        pageSize: this.pageSize()
      })
      .subscribe({
        next: (r) => {
          this.agents.set(r.items);
          this.total.set(r.total);
          this.loading.set(false);
        },
        error: (err) => {
          this.loading.set(false);
          this.notify.error(err);
        }
      });
  }

  onPage(event: PageEvent): void {
    this.page.set(event.pageIndex);
    this.pageSize.set(event.pageSize);
    this.reload();
  }

  toggleStatus(agent: AgentListItem): void {
    this.actions.toggleStatus(agent).subscribe(() => this.reload());
  }

  duplicate(agent: AgentListItem): void {
    this.actions.duplicate(agent).subscribe((copy) => this.router.navigate(['/agents', copy.id]));
  }

  versions(agent: AgentListItem): void {
    this.actions.versions(agent, this.canManage()).subscribe(() => this.reload());
  }
}
