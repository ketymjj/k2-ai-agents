import { DatePipe } from '@angular/common';
import { Component, DestroyRef, OnInit, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormControl, ReactiveFormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatDialog } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatMenuModule } from '@angular/material/menu';
import { MatPaginatorModule, PageEvent } from '@angular/material/paginator';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { MatSelectModule } from '@angular/material/select';
import { RouterLink } from '@angular/router';
import { TranslocoPipe, TranslocoService } from '@jsverse/transloco';
import { debounceTime, distinctUntilChanged, filter } from 'rxjs';
import { CompaniesApi } from '../../../core/api/companies.api';
import { Company, CompanyStatus } from '../../../core/models';
import { NotifyService } from '../../../core/notify.service';
import { confirmAction } from '../../../shared/confirm-dialog/confirm-dialog';
import { DocumentPipe } from '../../../shared/document.pipe';
import { CompanyDialog } from '../company-dialog/company-dialog';
import { ModuleGuide } from '../../../shared/module-guide/module-guide';

@Component({
  selector: 'app-companies-list',
  imports: [ModuleGuide, 
    DatePipe, RouterLink, ReactiveFormsModule, MatButtonModule, MatIconModule, MatFormFieldModule, MatInputModule,
    MatSelectModule, MatMenuModule, MatPaginatorModule, MatProgressBarModule, TranslocoPipe, DocumentPipe
  ],
  templateUrl: './companies-list.html',
  styleUrl: './companies-list.scss'
})
export class CompaniesList implements OnInit {
  private readonly api = inject(CompaniesApi);
  private readonly dialog = inject(MatDialog);
  private readonly notify = inject(NotifyService);
  private readonly transloco = inject(TranslocoService);
  private readonly destroyRef = inject(DestroyRef);

  protected readonly search = new FormControl('', { nonNullable: true });
  protected readonly status = new FormControl<CompanyStatus | ''>('', { nonNullable: true });

  protected readonly companies = signal<Company[]>([]);
  protected readonly total = signal(0);
  protected readonly page = signal(0);
  protected readonly pageSize = signal(20);
  protected readonly loading = signal(false);
  protected readonly failed = signal(false);

  ngOnInit(): void {
    this.search.valueChanges
      .pipe(debounceTime(300), distinctUntilChanged(), takeUntilDestroyed(this.destroyRef))
      .subscribe(() => this.reload(true));
    this.status.valueChanges.pipe(takeUntilDestroyed(this.destroyRef)).subscribe(() => this.reload(true));
    this.reload();
  }

  reload(resetPage = false): void {
    if (resetPage) this.page.set(0);
    this.loading.set(true);
    this.failed.set(false);
    this.api
      .list({ search: this.search.value, status: this.status.value, page: this.page() + 1, pageSize: this.pageSize() })
      .subscribe({
        next: (result) => {
          this.companies.set(result.items);
          this.total.set(result.total);
          this.loading.set(false);
        },
        error: (err) => {
          this.loading.set(false);
          this.failed.set(true);
          this.notify.error(err);
        }
      });
  }

  onPage(event: PageEvent): void {
    this.page.set(event.pageIndex);
    this.pageSize.set(event.pageSize);
    this.reload();
  }

  openDialog(company: Company | null = null): void {
    this.dialog
      .open(CompanyDialog, { data: company, width: '640px', maxWidth: '95vw' })
      .afterClosed()
      .pipe(filter(Boolean))
      .subscribe(() => {
        this.notify.success(company ? 'companies.updated' : 'companies.created');
        this.reload();
      });
  }

  toggleStatus(company: Company): void {
    const activating = company.status !== 'Active';
    const t = (key: string) => this.transloco.translate(key, { name: company.name });
    confirmAction(this.dialog, {
      title: t(activating ? 'companies.confirmActivateTitle' : 'companies.confirmDeactivateTitle'),
      message: t(activating ? 'companies.confirmActivate' : 'companies.confirmDeactivate'),
      confirmLabel: t(activating ? 'common.activate' : 'common.deactivate'),
      danger: !activating
    })
      .pipe(filter(Boolean))
      .subscribe(() =>
        this.api.setStatus(company.id, activating ? 'Active' : 'Inactive').subscribe({
          next: () => {
            this.notify.success(activating ? 'companies.activated' : 'companies.deactivated');
            this.reload();
          },
          error: (err) => this.notify.error(err)
        })
      );
  }
}
