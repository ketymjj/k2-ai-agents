import { Component, inject, signal } from '@angular/core';
import { AbstractControl, NonNullableFormBuilder, ReactiveFormsModule, ValidationErrors, Validators } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { TranslocoPipe } from '@jsverse/transloco';
import { AuthService } from '../../core/auth/auth.service';
import { errorMessage } from '../../core/http-error';
import { NotifyService } from '../../core/notify.service';
import { STRONG_PASSWORD } from '../../shared/validators';

function matchPasswords(group: AbstractControl): ValidationErrors | null {
  return group.get('newPassword')?.value === group.get('confirmPassword')?.value ? null : { mismatch: true };
}

@Component({
  selector: 'app-change-password-dialog',
  imports: [ReactiveFormsModule, MatDialogModule, MatButtonModule, MatFormFieldModule, MatInputModule, TranslocoPipe],
  templateUrl: './change-password-dialog.html',
  styleUrl: './change-password-dialog.scss'
})
export class ChangePasswordDialog {
  private readonly auth = inject(AuthService);
  private readonly notify = inject(NotifyService);
  private readonly ref = inject(MatDialogRef<ChangePasswordDialog>);

  protected readonly saving = signal(false);
  protected readonly serverError = signal<string | null>(null);

  protected readonly form = inject(NonNullableFormBuilder).group(
    {
      currentPassword: ['', Validators.required],
      newPassword: ['', [Validators.required, Validators.pattern(STRONG_PASSWORD)]],
      confirmPassword: ['', Validators.required]
    },
    { validators: matchPasswords }
  );

  submit(): void {
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }
    this.saving.set(true);
    this.serverError.set(null);
    const { currentPassword, newPassword } = this.form.getRawValue();
    this.auth.changePassword(currentPassword, newPassword).subscribe({
      next: () => {
        this.ref.close();
        this.notify.success('account.changed');
        // A API encerra todas as sessões ao trocar a senha.
        this.auth.logout();
      },
      error: (err) => {
        this.saving.set(false);
        this.serverError.set(errorMessage(err));
      }
    });
  }
}
