import { Component, computed, inject, signal } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { NonNullableFormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MAT_DIALOG_DATA, MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatSelectModule } from '@angular/material/select';
import { MatSlideToggleModule } from '@angular/material/slide-toggle';
import { TranslocoPipe, TranslocoService } from '@jsverse/transloco';
import { AutomationsApi, SaveAutomation } from '../../../core/api/automations.api';
import { errorMessage } from '../../../core/http-error';
import { AutomationAction, AutomationCatalog, AutomationRule, AutomationTrigger, Company } from '../../../core/models';

export interface AutomationDialogData {
  rule: AutomationRule | null;
  catalog: AutomationCatalog;
  companies: Company[];
  isSuperAdmin: boolean;
}

type Field = 'title' | 'message';

@Component({
  selector: 'app-automation-dialog',
  imports: [ReactiveFormsModule, MatDialogModule, MatButtonModule, MatFormFieldModule, MatIconModule, MatInputModule, MatSelectModule, MatSlideToggleModule, TranslocoPipe],
  templateUrl: './automation-dialog.html',
  styleUrl: './automation-dialog.scss'
})
export class AutomationDialog {
  private readonly api = inject(AutomationsApi);
  private readonly ref = inject(MatDialogRef<AutomationDialog, AutomationRule>);
  private readonly fb = inject(NonNullableFormBuilder);
  private readonly transloco = inject(TranslocoService);
  protected readonly data = inject<AutomationDialogData>(MAT_DIALOG_DATA);

  protected readonly triggers = Object.keys(this.data.catalog.allowed) as AutomationTrigger[];
  protected readonly saving = signal(false);
  protected readonly error = signal<string | null>(null);
  private lastField: Field = 'message';

  private readonly rule = this.data.rule;
  protected readonly form = this.fb.group({
    companyId: [this.rule?.companyId ?? '', this.data.isSuperAdmin && !this.rule ? Validators.required : []],
    name: [this.rule?.name ?? '', [Validators.required, Validators.maxLength(120)]],
    triggerType: [this.rule?.triggerType ?? this.triggers[0], Validators.required],
    actionType: [this.rule?.actionType ?? this.data.catalog.allowed[this.triggers[0]][0], Validators.required],
    title: [this.rule?.configuration.title ?? '', Validators.maxLength(160)],
    message: [this.rule?.configuration.message ?? '', Validators.maxLength(500)],
    enabled: [this.rule?.enabled ?? true]
  });

  private readonly value = toSignal(this.form.valueChanges, { initialValue: this.form.getRawValue() });
  protected readonly actions = computed(() => this.data.catalog.allowed[this.value().triggerType as AutomationTrigger] ?? []);
  protected readonly usesText = computed(() => this.value().actionType !== 'RecordMetric');
  protected readonly preview = computed(() => ({ title: this.render(this.value().title ?? ''), message: this.render(this.value().message ?? '') }));

  constructor() {
    // Ao trocar o gatilho, mantém a ação se ela continuar permitida; senão escolhe a primeira válida.
    this.form.controls.triggerType.valueChanges.subscribe((t) => {
      const allowed = this.data.catalog.allowed[t] ?? [];
      if (!allowed.includes(this.form.controls.actionType.value)) this.form.controls.actionType.setValue(allowed[0]);
    });
  }

  focus(field: Field): void {
    this.lastField = field;
  }

  /** Insere o placeholder no cursor do último campo de texto focado (título ou mensagem). */
  insert(placeholder: string, titleInput: HTMLInputElement, messageInput: HTMLTextAreaElement): void {
    const input = this.lastField === 'title' ? titleInput : messageInput;
    const control = this.form.controls[this.lastField];
    const text = control.value;
    const token = `{${placeholder}}`;
    const at = input.selectionStart ?? text.length;
    const end = input.selectionEnd ?? at;
    control.setValue(text.slice(0, at) + token + text.slice(end));
    queueMicrotask(() => {
      input.focus();
      input.setSelectionRange(at + token.length, at + token.length);
    });
  }

  save(): void {
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }
    const v = this.form.getRawValue();
    const body: SaveAutomation = {
      companyId: v.companyId || null,
      name: v.name.trim(),
      triggerType: v.triggerType as AutomationTrigger,
      actionType: v.actionType as AutomationAction,
      configuration: this.usesText() ? { title: v.title.trim() || null, message: v.message.trim() || null } : { title: null, message: null },
      enabled: v.enabled
    };
    this.saving.set(true);
    this.error.set(null);
    (this.rule ? this.api.update(this.rule.id, body) : this.api.create(body)).subscribe({
      next: (saved) => this.ref.close(saved),
      error: (err) => {
        this.saving.set(false);
        this.error.set(errorMessage(err));
      }
    });
  }

  /** Preenche os placeholders com valores de exemplo (traduzidos), só para a pré-visualização. */
  private render(template: string): string {
    return template.replace(/\{(\w+)\}/g, (m, key: string) =>
      this.data.catalog.placeholders.includes(key) ? this.transloco.translate('automations.sample.' + key) : m);
  }
}
