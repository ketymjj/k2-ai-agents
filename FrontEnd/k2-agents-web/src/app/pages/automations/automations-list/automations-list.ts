import { DatePipe } from '@angular/common';
import { Component, OnInit, computed, inject, signal } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatDialog } from '@angular/material/dialog';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { MatSlideToggleChange, MatSlideToggleModule } from '@angular/material/slide-toggle';
import { MatTooltipModule } from '@angular/material/tooltip';
import { TranslocoPipe, TranslocoService } from '@jsverse/transloco';
import { filter, forkJoin } from 'rxjs';
import { AutomationsApi } from '../../../core/api/automations.api';
import { CompaniesApi } from '../../../core/api/companies.api';
import { AuthService } from '../../../core/auth/auth.service';
import { AutomationCatalog, AutomationRule, Company } from '../../../core/models';
import { NotifyService } from '../../../core/notify.service';
import { confirmAction } from '../../../shared/confirm-dialog/confirm-dialog';
import { AutomationDialog, AutomationDialogData } from '../automation-dialog/automation-dialog';
import { ModuleGuide } from '../../../shared/module-guide/module-guide';

@Component({
  selector: 'app-automations-list',
  imports: [ModuleGuide, DatePipe, MatButtonModule, MatIconModule, MatProgressBarModule, MatSlideToggleModule, MatTooltipModule, TranslocoPipe],
  templateUrl: './automations-list.html',
  styleUrl: './automations-list.scss'
})
export class AutomationsList implements OnInit {
  private readonly api = inject(AutomationsApi);
  private readonly companiesApi = inject(CompaniesApi);
  private readonly dialog = inject(MatDialog);
  private readonly notify = inject(NotifyService);
  private readonly transloco = inject(TranslocoService);
  protected readonly auth = inject(AuthService);

  protected readonly canManage = computed(() => this.auth.hasRole('SuperAdmin', 'CompanyAdmin'));
  protected readonly rules = signal<AutomationRule[]>([]);
  protected readonly catalog = signal<AutomationCatalog | null>(null);
  protected readonly companies = signal<Company[]>([]);
  protected readonly loading = signal(true);
  protected readonly busy = signal<string | null>(null);

  ngOnInit(): void {
    if (this.auth.isSuperAdmin()) {
      this.companiesApi.list({ status: 'Active', pageSize: 100 }).subscribe({ next: (r) => this.companies.set(r.items), error: () => undefined });
    }
    forkJoin([this.api.catalog(), this.api.list()]).subscribe({
      next: ([catalog, rules]) => {
        this.catalog.set(catalog);
        this.rules.set(rules);
        this.loading.set(false);
      },
      error: (err) => {
        this.loading.set(false);
        this.notify.error(err);
      }
    });
  }

  open(rule: AutomationRule | null): void {
    const catalog = this.catalog();
    if (!catalog) return;
    this.dialog
      .open<AutomationDialog, AutomationDialogData, AutomationRule>(AutomationDialog, {
        data: { rule, catalog, companies: this.companies(), isSuperAdmin: this.auth.isSuperAdmin() },
        width: '640px',
        maxWidth: '95vw'
      })
      .afterClosed()
      .pipe(filter(Boolean))
      .subscribe((saved) => {
        this.rules.update((list) => (rule ? list.map((r) => (r.id === saved.id ? saved : r)) : [...list, saved]));
        this.notify.success(rule ? 'automations.updated' : 'automations.created');
      });
  }

  toggle(rule: AutomationRule, event: MatSlideToggleChange): void {
    this.busy.set(rule.id);
    this.api.setEnabled(rule.id, event.checked).subscribe({
      next: (saved) => {
        this.busy.set(null);
        this.rules.update((list) => list.map((r) => (r.id === saved.id ? saved : r)));
        this.notify.success(saved.enabled ? 'automations.enabledOn' : 'automations.enabledOff');
      },
      error: (err) => {
        this.busy.set(null);
        event.source.checked = rule.enabled;
        this.notify.error(err);
      }
    });
  }

  remove(rule: AutomationRule): void {
    const t = (key: string) => this.transloco.translate(key, { name: rule.name });
    confirmAction(this.dialog, {
      title: t('automations.deleteTitle'),
      message: t('automations.deleteConfirm'),
      confirmLabel: t('common.delete'),
      danger: true
    })
      .pipe(filter(Boolean))
      .subscribe(() =>
        this.api.delete(rule.id).subscribe({
          next: () => {
            this.rules.update((list) => list.filter((r) => r.id !== rule.id));
            this.notify.success('automations.deleted');
          },
          error: (err) => this.notify.error(err)
        })
      );
  }
}
