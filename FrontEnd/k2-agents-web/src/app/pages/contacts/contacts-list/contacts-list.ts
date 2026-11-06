import { DatePipe } from '@angular/common';
import { Component, DestroyRef, OnInit, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormControl, ReactiveFormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatDialog } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatPaginatorModule, PageEvent } from '@angular/material/paginator';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { MatSelectModule } from '@angular/material/select';
import { Router, RouterLink } from '@angular/router';
import { TranslocoPipe } from '@jsverse/transloco';
import { debounceTime, distinctUntilChanged, filter, merge, of } from 'rxjs';
import { CompaniesApi } from '../../../core/api/companies.api';
import { ContactsApi } from '../../../core/api/contacts.api';
import { AuthService } from '../../../core/auth/auth.service';
import { CONTACT_STATUSES, Company, Contact, ContactStatus, Funnel, INTEREST_LEVELS, InterestLevel } from '../../../core/models';
import { NotifyService } from '../../../core/notify.service';
import { ContactForm, ContactFormData } from '../contact-form/contact-form';
import { ModuleGuide } from '../../../shared/module-guide/module-guide';

@Component({
  selector: 'app-contacts-list',
  imports: [ModuleGuide, 
    DatePipe, RouterLink, ReactiveFormsModule, MatButtonModule, MatIconModule, MatFormFieldModule, MatInputModule, MatSelectModule,
    MatPaginatorModule, MatProgressBarModule, TranslocoPipe
  ],
  templateUrl: './contacts-list.html',
  styleUrl: './contacts-list.scss'
})
export class ContactsList implements OnInit {
  private readonly api = inject(ContactsApi);
  private readonly companiesApi = inject(CompaniesApi);
  private readonly dialog = inject(MatDialog);
  private readonly notify = inject(NotifyService);
  private readonly router = inject(Router);
  private readonly destroyRef = inject(DestroyRef);
  protected readonly auth = inject(AuthService);
  protected readonly formatPhone = ContactsApi.formatPhone;

  protected readonly interests = INTEREST_LEVELS;
  protected readonly canEdit = computed(() => this.auth.hasRole('SuperAdmin', 'CompanyAdmin', 'AgentManager', 'Attendant'));

  protected readonly stage = signal<ContactStatus | ''>('');
  protected readonly search = new FormControl('', { nonNullable: true });
  protected readonly leadsOnly = new FormControl<'' | 'true'>('', { nonNullable: true });
  protected readonly interest = new FormControl<InterestLevel | ''>('', { nonNullable: true });

  protected readonly contacts = signal<Contact[]>([]);
  protected readonly funnel = signal<Funnel | null>(null);
  protected readonly companies = signal<Company[]>([]);
  protected readonly total = signal(0);
  protected readonly page = signal(0);
  protected readonly pageSize = signal(20);
  protected readonly loading = signal(false);

  protected readonly stages = computed(() => {
    const f = this.funnel();
    return CONTACT_STATUSES.map((s) => ({ status: s, count: f?.stages.find((x) => x.status === s)?.count ?? 0 }));
  });
  protected readonly maxStage = computed(() => Math.max(1, ...this.stages().map((s) => s.count)));

  ngOnInit(): void {
    if (this.auth.isSuperAdmin()) {
      this.companiesApi.list({ status: 'Active', pageSize: 100 }).subscribe({ next: (r) => this.companies.set(r.items), error: () => undefined });
    }
    merge(
      this.search.valueChanges.pipe(debounceTime(300), distinctUntilChanged()),
      this.leadsOnly.valueChanges,
      this.interest.valueChanges,
      of(null)
    )
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe((v) => this.reload(v !== null));
    this.loadFunnel();
  }

  setStage(stage: ContactStatus | ''): void {
    this.stage.set(this.stage() === stage ? '' : stage);
    this.reload(true);
  }

  reload(resetPage = false): void {
    if (resetPage) this.page.set(0);
    this.loading.set(true);
    this.api
      .list({
        status: this.stage(),
        search: this.search.value,
        isLead: this.leadsOnly.value === 'true' ? true : '',
        interest: this.interest.value,
        page: this.page() + 1,
        pageSize: this.pageSize()
      })
      .subscribe({
        next: (r) => {
          this.contacts.set(r.items);
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

  create(): void {
    this.dialog
      .open<ContactForm, ContactFormData, Contact>(ContactForm, {
        data: { contact: null, companies: this.companies(), isSuperAdmin: this.auth.isSuperAdmin() },
        width: '640px',
        maxWidth: '95vw'
      })
      .afterClosed()
      .pipe(filter(Boolean))
      .subscribe((c) => {
        this.notify.success('contacts.created');
        this.router.navigate(['/contacts', c.id]);
      });
  }

  private loadFunnel(): void {
    this.api.funnel().subscribe({ next: (f) => this.funnel.set(f), error: () => undefined });
  }
}
