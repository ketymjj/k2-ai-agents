import { Component, inject, signal } from '@angular/core';
import { NonNullableFormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MAT_DIALOG_DATA, MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { TranslocoPipe } from '@jsverse/transloco';
import { CostMonth, ReportsApi } from '../../../core/api/reports.api';
import { errorMessage } from '../../../core/http-error';

export interface BillingDialogData {
  month: CostMonth;
}

@Component({
  selector: 'app-billing-dialog',
  imports: [ReactiveFormsModule, MatDialogModule, MatButtonModule, MatFormFieldModule, MatInputModule, TranslocoPipe],
  templateUrl: './billing-dialog.html',
  styleUrl: './billing-dialog.scss'
})
export class BillingDialog {
  private readonly api = inject(ReportsApi);
  private readonly ref = inject(MatDialogRef<BillingDialog, CostMonth>);
  protected readonly data = inject<BillingDialogData>(MAT_DIALOG_DATA);
  protected readonly saving = signal(false);
  protected readonly error = signal<string | null>(null);

  protected readonly form = inject(NonNullableFormBuilder).group({
    amountUsd: [this.data.month.billedUsd ?? (null as unknown as number), [Validators.required, Validators.min(0), Validators.max(9_999_999)]],
    notes: [this.data.month.billedNotes ?? '', Validators.maxLength(500)]
  });

  save(): void {
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }
    const v = this.form.getRawValue();
    this.saving.set(true);
    this.error.set(null);
    this.api.saveBilling(this.data.month.month, Number(v.amountUsd), v.notes.trim() || null).subscribe({
      next: (saved) => this.ref.close(saved),
      error: (err) => {
        this.saving.set(false);
        this.error.set(errorMessage(err));
      }
    });
  }
}
