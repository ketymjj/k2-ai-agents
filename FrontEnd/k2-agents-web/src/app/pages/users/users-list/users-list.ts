import { DatePipe } from '@angular/common';
import { Component, DestroyRef, OnInit, inject, input, signal } from '@angular/core';
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
import { TranslocoPipe, TranslocoService } from '@jsverse/transloco';
import { debounceTime, distinctUntilChanged, filter, merge, of } from 'rxjs';
import { CompaniesApi } from '../../../core/api/companies.api';
import { UsersApi } from '../../../core/api/users.api';
import { AuthService } from '../../../core/auth/auth.service';
import { Company, Role, RoleInfo, User } from '../../../core/models';
import { NotifyService } from '../../../core/notify.service';
import { confirmAction } from '../../../shared/confirm-dialog/confirm-dialog';
import { UserDialog, UserDialogData } from '../user-dialog/user-dialog';
import { ModuleGuide } from '../../../shared/module-guide/module-guide';

@Component({
  selector: 'app-users-list',
  imports: [ModuleGuide, 
    DatePipe, ReactiveFormsModule, MatButtonModule, MatIconModule, MatFormFieldModule, MatInputModule, MatSelectModule,
    MatMenuModule, MatPaginatorModule, MatProgressBarModule, TranslocoPipe
  ],
  templateUrl: './users-list.html',
  styleUrl: './users-list.scss'
})
export class UsersList implements OnInit {
  private readonly api = inject(UsersApi);
  private readonly companiesApi = inject(CompaniesApi);
  private readonly dialog = inject(MatDialog);
  private readonly notify = inject(NotifyService);
  private readonly transloco = inject(TranslocoService);
  private readonly destroyRef = inject(DestroyRef);
  protected readonly auth = inject(AuthService);

  /** Query param opcional vindo do detalhe da empresa. */
  readonly companyId = input<string>();

  protected readonly search = new FormControl('', { nonNullable: true });
  protected readonly role = new FormControl<Role | ''>('', { nonNullable: true });
  protected readonly active = new FormControl<'' | 'true' | 'false'>('', { nonNullable: true });
  protected readonly company = new FormControl('', { nonNullable: true });

  protected readonly users = signal<User[]>([]);
  protected readonly roles = signal<RoleInfo[]>([]);
  protected readonly companies = signal<Company[]>([]);
  protected readonly total = signal(0);
  protected readonly page = signal(0);
  protected readonly pageSize = signal(20);
  protected readonly loading = signal(false);

  ngOnInit(): void {
    this.company.setValue(this.companyId() ?? '', { emitEvent: false });
    this.api.roles().subscribe({ next: (roles) => this.roles.set(roles), error: (e) => this.notify.error(e) });
    if (this.auth.isSuperAdmin()) {
      this.companiesApi.list({ pageSize: 100 }).subscribe({ next: (r) => this.companies.set(r.items), error: (e) => this.notify.error(e) });
    }

    merge(
      this.search.valueChanges.pipe(debounceTime(300), distinctUntilChanged()),
      this.role.valueChanges,
      this.active.valueChanges,
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
        role: this.role.value,
        isActive: this.active.value === '' ? '' : this.active.value === 'true',
        companyId: this.company.value || undefined,
        page: this.page() + 1,
        pageSize: this.pageSize()
      })
      .subscribe({
        next: (result) => {
          this.users.set(result.items);
          this.total.set(result.total);
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

  openDialog(user: User | null = null): void {
    const data: UserDialogData = {
      user,
      roles: this.roles(),
      companies: this.companies().filter((c) => c.status === 'Active'),
      defaultCompanyId: this.company.value || null
    };
    this.dialog
      .open(UserDialog, { data, width: '640px', maxWidth: '95vw' })
      .afterClosed()
      .pipe(filter(Boolean))
      .subscribe(() => {
        this.notify.success(user ? 'users.updated' : 'users.created');
        this.reload();
      });
  }

  toggleActive(user: User): void {
    const activating = !user.isActive;
    const t = (key: string) => this.transloco.translate(key, { name: user.name });
    confirmAction(this.dialog, {
      title: t(activating ? 'users.confirmActivateTitle' : 'users.confirmDeactivateTitle'),
      message: t(activating ? 'users.confirmActivate' : 'users.confirmDeactivate'),
      confirmLabel: t(activating ? 'common.activate' : 'common.deactivate'),
      danger: !activating
    })
      .pipe(filter(Boolean))
      .subscribe(() =>
        this.api.setActive(user.id, activating).subscribe({
          next: () => {
            this.notify.success(activating ? 'users.activated' : 'users.deactivated');
            this.reload();
          },
          error: (err) => this.notify.error(err)
        })
      );
  }

  isSelf(user: User): boolean {
    return user.id === this.auth.user()?.id;
  }
}
