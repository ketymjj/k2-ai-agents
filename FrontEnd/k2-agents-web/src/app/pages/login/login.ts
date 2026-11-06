import { Component, inject, input, signal } from '@angular/core';
import { NonNullableFormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatMenuModule } from '@angular/material/menu';
import { Router } from '@angular/router';
import { TranslocoPipe } from '@jsverse/transloco';
import { AuthService } from '../../core/auth/auth.service';
import { errorMessage } from '../../core/http-error';
import { LanguageService } from '../../core/language.service';
import { Language } from '../../core/models';
import { ThemeService } from '../../core/theme.service';

@Component({
  selector: 'app-login',
  imports: [ReactiveFormsModule, MatFormFieldModule, MatInputModule, MatButtonModule, MatIconModule, MatMenuModule, TranslocoPipe],
  templateUrl: './login.html',
  styleUrl: './login.scss'
})
export class Login {
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);
  protected readonly theme = inject(ThemeService);
  protected readonly language = inject(LanguageService);

  /** Query params (withComponentInputBinding). */
  readonly returnUrl = input<string>();
  readonly expired = input<string>();

  protected readonly submitting = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly showPassword = signal(false);
  protected readonly languages: Language[] = ['pt-BR', 'en'];

  protected readonly form = inject(NonNullableFormBuilder).group({
    email: ['', [Validators.required, Validators.email]],
    password: ['', Validators.required]
  });

  submit(): void {
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }
    this.submitting.set(true);
    this.error.set(null);
    const { email, password } = this.form.getRawValue();
    this.auth.login(email, password).subscribe({
      next: () => {
        const target = this.returnUrl();
        // Só aceita rotas internas, evitando redirecionamento aberto.
        this.router.navigateByUrl(target && target.startsWith('/') && !target.startsWith('//') ? target : '/');
      },
      error: (err) => {
        this.submitting.set(false);
        this.error.set(errorMessage(err));
      }
    });
  }
}
