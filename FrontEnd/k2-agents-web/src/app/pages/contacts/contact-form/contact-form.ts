import { Component, inject, signal } from '@angular/core';
import { NonNullableFormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatCheckboxModule } from '@angular/material/checkbox';
import { MAT_DIALOG_DATA, MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatSelectModule } from '@angular/material/select';
import { TranslocoPipe } from '@jsverse/transloco';
import { ContactsApi } from '../../../core/api/contacts.api';
import { errorMessage } from '../../../core/http-error';
import { CONTACT_STATUSES, Company, Contact, ContactStatus, INTEREST_LEVELS, InterestLevel } from '../../../core/models';

export interface ContactFormData {
  contact: Contact | null;
  companies: Company[];
  isSuperAdmin: boolean;
}

@Component({
  selector: 'app-contact-form',
  imports: [ReactiveFormsModule, MatDialogModule, MatButtonModule, MatCheckboxModule, MatFormFieldModule, MatInputModule, MatSelectModule, TranslocoPipe],
  templateUrl: './contact-form.html',
  styleUrl: './contact-form.scss'
})
export class ContactForm {
  private readonly api = inject(ContactsApi);
  private readonly ref = inject(MatDialogRef<ContactForm, Contact>);
  protected readonly data = inject<ContactFormData>(MAT_DIALOG_DATA);

  protected readonly statuses = CONTACT_STATUSES;
  protected readonly interests = INTEREST_LEVELS;
  protected readonly saving = signal(false);
  protected readonly serverError = signal<string | null>(null);
  protected readonly showCompany = this.data.isSuperAdmin && !this.data.contact;
  private readonly c = this.data.contact;

  protected readonly form = inject(NonNullableFormBuilder).group({
    companyId: [this.c?.companyId ?? ''],
    name: [this.c?.name ?? '', [Validators.required, Validators.maxLength(120)]],
    email: [this.c?.email ?? '', [Validators.email, Validators.maxLength(160)]],
    phone: [this.c?.phone ?? '', Validators.maxLength(32)],
    source: [this.c?.source ?? 'Manual', Validators.maxLength(40)],
    status: [this.c?.status ?? ('New' as ContactStatus)],
    interest: [this.c?.interest ?? ('Unknown' as InterestLevel)],
    isLead: [this.c?.isLead ?? false],
    notes: [this.c?.notes ?? '', Validators.maxLength(2000)]
  });

  constructor() {
    if (this.showCompany) this.form.controls.companyId.addValidators(Validators.required);
  }

  submit(): void {
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }
    const v = this.form.getRawValue();
    const body = {
      companyId: this.showCompany ? v.companyId : null,
      name: v.name,
      email: v.email || null,
      phone: v.phone || null,
      source: v.source || null,
      status: v.status,
      interest: v.interest,
      isLead: v.isLead,
      notes: v.notes || null
    };
    this.saving.set(true);
    this.serverError.set(null);
    (this.c ? this.api.update(this.c.id, body) : this.api.create(body)).subscribe({
      next: (saved) => this.ref.close(saved),
      error: (err) => {
        this.saving.set(false);
        this.serverError.set(errorMessage(err));
      }
    });
  }
}
