import { Component, inject, signal } from '@angular/core';
import { NonNullableFormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MAT_DIALOG_DATA, MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { TranslocoPipe } from '@jsverse/transloco';
import { CompaniesApi } from '../../../core/api/companies.api';
import { errorMessage, fieldErrors } from '../../../core/http-error';
import { Company, SaveCompany } from '../../../core/models';
import { emptyToNull, httpUrl } from '../../../shared/validators';

@Component({
  selector: 'app-company-dialog',
  imports: [ReactiveFormsModule, MatDialogModule, MatButtonModule, MatFormFieldModule, MatInputModule, TranslocoPipe],
  templateUrl: './company-dialog.html',
  styleUrl: './company-dialog.scss'
})
export class CompanyDialog {
  private readonly api = inject(CompaniesApi);
  private readonly ref = inject(MatDialogRef<CompanyDialog, Company>);
  protected readonly company = inject<Company | null>(MAT_DIALOG_DATA);

  protected readonly saving = signal(false);
  protected readonly serverError = signal<string | null>(null);
  protected readonly serverFieldErrors = signal<Record<string, string>>({});

  protected readonly form = inject(NonNullableFormBuilder).group({
    name: [this.company?.name ?? '', [Validators.required, Validators.maxLength(160)]],
    document: [this.company?.document ?? ''],
    email: [this.company?.email ?? '', [Validators.email, Validators.maxLength(160)]],
    phone: [this.company?.phone ?? '', Validators.maxLength(32)],
    industry: [this.company?.industry ?? '', Validators.maxLength(80)],
    website: [this.company?.website ?? '', [httpUrl, Validators.maxLength(255)]],
    conversationRetentionDays: [this.company?.conversationRetentionDays?.toString() ?? '', [Validators.min(1), Validators.max(3650)]]
  });

  submit(): void {
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }
    this.saving.set(true);
    this.serverError.set(null);
    this.serverFieldErrors.set({});

    const raw = emptyToNull(this.form.getRawValue());
    const body: SaveCompany = {
      ...raw,
      name: raw.name!,
      conversationRetentionDays: raw.conversationRetentionDays ? Number(raw.conversationRetentionDays) : null
    };
    const request = this.company ? this.api.update(this.company.id, body) : this.api.create(body);
    request.subscribe({
      next: (saved) => this.ref.close(saved),
      error: (err) => {
        this.saving.set(false);
        this.serverError.set(errorMessage(err));
        this.serverFieldErrors.set(fieldErrors(err));
      }
    });
  }
}
