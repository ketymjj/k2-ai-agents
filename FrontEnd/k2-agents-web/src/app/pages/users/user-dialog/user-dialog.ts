import { Component, computed, inject, signal } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { NonNullableFormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MAT_DIALOG_DATA, MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatSelectModule } from '@angular/material/select';
import { TranslocoPipe } from '@jsverse/transloco';
import { startWith } from 'rxjs';
import { UsersApi } from '../../../core/api/users.api';
import { AuthService } from '../../../core/auth/auth.service';
import { errorMessage } from '../../../core/http-error';
import { Company, Language, Role, RoleInfo, User } from '../../../core/models';
import { STRONG_PASSWORD } from '../../../shared/validators';

export interface UserDialogData {
  user: User | null;
  roles: RoleInfo[];
  companies: Company[];
  defaultCompanyId: string | null;
}

@Component({
  selector: 'app-user-dialog',
  imports: [ReactiveFormsModule, MatDialogModule, MatButtonModule, MatFormFieldModule, MatInputModule, MatSelectModule, TranslocoPipe],
  templateUrl: './user-dialog.html',
  styleUrl: './user-dialog.scss'
})
export class UserDialog {
  private readonly api = inject(UsersApi);
  private readonly ref = inject(MatDialogRef<UserDialog, User>);
  protected readonly auth = inject(AuthService);
  protected readonly data = inject<UserDialogData>(MAT_DIALOG_DATA);

  protected readonly isEdit = this.data.user !== null;
  protected readonly isSelf = this.data.user?.id === this.auth.user()?.id;
  protected readonly saving = signal(false);
  protected readonly serverError = signal<string | null>(null);
  protected readonly languages: Language[] = ['pt-BR', 'en'];

  /** Edição não troca entre perfil de plataforma e de empresa (regra da API). */
  protected readonly availableRoles = this.data.roles.filter(
    (r) => !this.isEdit || (r.id === 'SuperAdmin') === (this.data.user!.role === 'SuperAdmin')
  );

  protected readonly form = inject(NonNullableFormBuilder).group({
    name: [this.data.user?.name ?? '', [Validators.required, Validators.maxLength(120)]],
    email: [this.data.user?.email ?? '', [Validators.required, Validators.email, Validators.maxLength(160)]],
    role: [this.data.user?.role ?? ('Attendant' as Role), Validators.required],
    companyId: [this.data.user?.companyId ?? this.data.defaultCompanyId ?? ''],
    password: ['', this.isEdit ? [Validators.pattern(STRONG_PASSWORD)] : [Validators.required, Validators.pattern(STRONG_PASSWORD)]],
    preferredLanguage: [this.data.user?.preferredLanguage ?? ('pt-BR' as Language)]
  });

  private readonly role = toSignal(this.form.controls.role.valueChanges.pipe(startWith(this.form.controls.role.value)));

  /** Super Admin escolhe a empresa ao criar perfis de empresa; Company Admin usa sempre a própria. */
  protected readonly showCompany = computed(() => this.auth.isSuperAdmin() && !this.isEdit && this.role() !== 'SuperAdmin');

  constructor() {
    if (this.isSelf) this.form.controls.role.disable();
  }

  submit(): void {
    if (this.showCompany() && !this.form.controls.companyId.value) {
      this.form.controls.companyId.setErrors({ required: true });
    }
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }
    this.saving.set(true);
    this.serverError.set(null);
    const v = this.form.getRawValue();

    const request = this.isEdit
      ? this.api.update(this.data.user!.id, {
          name: v.name,
          email: v.email,
          role: v.role,
          password: v.password || null,
          preferredLanguage: v.preferredLanguage
        })
      : this.api.create({
          name: v.name,
          email: v.email,
          password: v.password,
          role: v.role,
          companyId: this.showCompany() ? v.companyId : null,
          preferredLanguage: v.preferredLanguage
        });

    request.subscribe({
      next: (user) => this.ref.close(user),
      error: (err) => {
        this.saving.set(false);
        this.serverError.set(errorMessage(err));
      }
    });
  }
}
