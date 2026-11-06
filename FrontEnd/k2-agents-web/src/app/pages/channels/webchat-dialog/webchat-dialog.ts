import { Component, computed, inject, signal } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { AbstractControl, NonNullableFormBuilder, ReactiveFormsModule, ValidationErrors, Validators } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatButtonToggleModule } from '@angular/material/button-toggle';
import { MAT_DIALOG_DATA, MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatSelectModule } from '@angular/material/select';
import { MatSlideToggleModule } from '@angular/material/slide-toggle';
import { TranslocoPipe } from '@jsverse/transloco';
import { startWith } from 'rxjs';
import { ChannelsApi } from '../../../core/api/channels.api';
import { errorMessage } from '../../../core/http-error';
import { AgentListItem, Channel, Company } from '../../../core/models';

export interface WebChatDialogData {
  channel: Channel | null;
  agents: AgentListItem[];
  companies: Company[];
  isSuperAdmin: boolean;
}

const HEX = /^#[0-9A-Fa-f]{6}$/;

/** Uma origem por linha, no formato https://dominio[:porta]. */
function originsValidator(control: AbstractControl): ValidationErrors | null {
  const lines = String(control.value ?? '').split('\n').map((l) => l.trim()).filter(Boolean);
  if (lines.length > 20) return { tooMany: true };
  for (const line of lines) {
    try {
      const url = new URL(line);
      if (url.protocol !== 'http:' && url.protocol !== 'https:') return { origin: line };
    } catch {
      return { origin: line };
    }
  }
  return null;
}

@Component({
  selector: 'app-webchat-dialog',
  imports: [
    ReactiveFormsModule, MatDialogModule, MatButtonModule, MatButtonToggleModule, MatIconModule, MatFormFieldModule, MatInputModule,
    MatSelectModule, MatSlideToggleModule, TranslocoPipe
  ],
  templateUrl: './webchat-dialog.html',
  styleUrl: './webchat-dialog.scss'
})
export class WebChatDialog {
  private readonly api = inject(ChannelsApi);
  private readonly ref = inject(MatDialogRef<WebChatDialog, Channel>);
  protected readonly data = inject<WebChatDialogData>(MAT_DIALOG_DATA);

  protected readonly saving = signal(false);
  protected readonly serverError = signal<string | null>(null);
  private readonly c = this.data.channel;

  protected readonly form = inject(NonNullableFormBuilder).group({
    companyId: [this.c?.companyId ?? ''],
    agentId: [this.c?.agentId ?? '', Validators.required],
    name: [this.c?.name ?? '', [Validators.required, Validators.maxLength(120)]],
    title: [this.c?.settings.title ?? 'Atendimento', [Validators.required, Validators.maxLength(60)]],
    subtitle: [this.c?.settings.subtitle ?? '', Validators.maxLength(100)],
    primaryColor: [this.c?.settings.primaryColor ?? '#6941C6', [Validators.required, Validators.pattern(HEX)]],
    position: [(this.c?.settings.position ?? 'right') as 'right' | 'left'],
    welcomeMessage: [this.c?.settings.welcomeMessage ?? '', Validators.maxLength(500)],
    allowSimulatedReplies: [this.c?.settings.allowSimulatedReplies ?? true],
    offlineMessage: [this.c?.settings.offlineMessage ?? '', Validators.maxLength(300)],
    allowedOrigins: [(this.c?.allowedOrigins ?? []).join('\n'), originsValidator]
  });

  private readonly value = toSignal(this.form.valueChanges.pipe(startWith(this.form.getRawValue())), { initialValue: this.form.getRawValue() });
  protected readonly previewColor = computed(() => (HEX.test(this.value().primaryColor ?? '') ? this.value().primaryColor! : '#6941C6'));
  protected readonly agents = computed(() => {
    const company = this.value().companyId;
    return this.data.isSuperAdmin && !this.c ? this.data.agents.filter((a) => a.companyId === company) : this.data.agents;
  });
  protected readonly showCompany = this.data.isSuperAdmin && !this.c;

  constructor() {
    if (this.showCompany) this.form.controls.companyId.addValidators(Validators.required);
    this.form.controls.companyId.valueChanges.subscribe(() => this.form.controls.agentId.setValue(''));
  }

  pickColor(event: Event): void {
    this.form.controls.primaryColor.setValue((event.target as HTMLInputElement).value.toUpperCase());
  }

  submit(): void {
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }
    const v = this.form.getRawValue();
    const body = {
      companyId: this.showCompany ? v.companyId : null,
      agentId: v.agentId,
      name: v.name,
      settings: {
        title: v.title,
        subtitle: v.subtitle || null,
        primaryColor: v.primaryColor,
        position: v.position,
        welcomeMessage: v.welcomeMessage || null,
        allowSimulatedReplies: v.allowSimulatedReplies,
        offlineMessage: v.offlineMessage || null
      },
      allowedOrigins: v.allowedOrigins.split('\n').map((l) => l.trim()).filter(Boolean)
    };
    this.saving.set(true);
    this.serverError.set(null);
    const request = this.c ? this.api.updateWebChat(this.c.id, body) : this.api.createWebChat(body);
    request.subscribe({
      next: (saved) => this.ref.close(saved),
      error: (err) => {
        this.saving.set(false);
        this.serverError.set(errorMessage(err));
      }
    });
  }
}
