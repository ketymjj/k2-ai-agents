import { DatePipe } from '@angular/common';
import { Component, computed, effect, inject, input, signal } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatDialog } from '@angular/material/dialog';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { RouterLink } from '@angular/router';
import { TranslocoPipe, TranslocoService } from '@jsverse/transloco';
import { filter } from 'rxjs';
import { ContactsApi } from '../../../core/api/contacts.api';
import { AuthService } from '../../../core/auth/auth.service';
import { CONTACT_STATUSES, Contact, ContactStatus, ConversationSummary } from '../../../core/models';
import { NotifyService } from '../../../core/notify.service';
import { confirmAction } from '../../../shared/confirm-dialog/confirm-dialog';
import { ContactForm, ContactFormData } from '../contact-form/contact-form';

@Component({
  selector: 'app-contact-detail',
  imports: [DatePipe, RouterLink, MatButtonModule, MatIconModule, MatProgressBarModule, TranslocoPipe],
  templateUrl: './contact-detail.html',
  styleUrl: './contact-detail.scss'
})
export class ContactDetail {
  private readonly api = inject(ContactsApi);
  private readonly dialog = inject(MatDialog);
  private readonly notify = inject(NotifyService);
  private readonly transloco = inject(TranslocoService);
  protected readonly auth = inject(AuthService);
  protected readonly formatPhone = ContactsApi.formatPhone;

  readonly id = input.required<string>();

  protected readonly statuses = CONTACT_STATUSES;
  protected readonly contact = signal<Contact | null>(null);
  protected readonly conversations = signal<ConversationSummary[]>([]);
  protected readonly loading = signal(true);
  protected readonly canEdit = computed(() => this.auth.hasRole('SuperAdmin', 'CompanyAdmin', 'AgentManager', 'Attendant') && !this.contact()?.isAnonymized);
  protected readonly canAnonymize = computed(() => this.auth.hasRole('SuperAdmin', 'CompanyAdmin') && !this.contact()?.isAnonymized);

  constructor() {
    effect(() => this.load(this.id()));
  }

  load(id: string): void {
    this.loading.set(true);
    this.api.get(id).subscribe({
      next: (d) => {
        this.contact.set(d.contact);
        this.conversations.set(d.conversations);
        this.loading.set(false);
      },
      error: (err) => {
        this.loading.set(false);
        this.notify.error(err);
      }
    });
  }

  moveTo(status: ContactStatus): void {
    const c = this.contact();
    if (!c || c.status === status || !this.canEdit()) return;
    this.api.setStatus(c.id, status).subscribe({
      next: (updated) => {
        this.contact.set(updated);
        this.notify.success('contacts.moved', { stage: this.transloco.translate('contactStatus.' + status) });
      },
      error: (err) => this.notify.error(err)
    });
  }

  edit(): void {
    this.dialog
      .open<ContactForm, ContactFormData, Contact>(ContactForm, {
        data: { contact: this.contact(), companies: [], isSuperAdmin: this.auth.isSuperAdmin() },
        width: '640px',
        maxWidth: '95vw'
      })
      .afterClosed()
      .pipe(filter(Boolean))
      .subscribe((c) => {
        this.contact.set(c);
        this.notify.success('contacts.updated');
      });
  }

  anonymize(): void {
    confirmAction(this.dialog, {
      title: this.transloco.translate('contacts.anonymizeTitle'),
      message: this.transloco.translate('contacts.anonymizeMessage'),
      confirmLabel: this.transloco.translate('contacts.anonymize'),
      danger: true
    })
      .pipe(filter(Boolean))
      .subscribe(() =>
        this.api.anonymize(this.contact()!.id).subscribe({
          next: () => {
            this.notify.success('contacts.anonymized');
            this.load(this.id());
          },
          error: (err) => this.notify.error(err)
        })
      );
  }
}
