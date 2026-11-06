import { Component, inject, signal } from '@angular/core';
import { NonNullableFormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MAT_DIALOG_DATA, MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatSelectModule } from '@angular/material/select';
import { TranslocoPipe } from '@jsverse/transloco';
import { ChannelsApi } from '../../../core/api/channels.api';
import { errorMessage, fieldErrors } from '../../../core/http-error';
import { AgentListItem, Channel, Company, SaveWhatsApp } from '../../../core/models';

export interface WhatsAppDialogData {
  channel: Channel | null;
  agents: AgentListItem[];
  companies: Company[];
  isSuperAdmin: boolean;
}

/** Configuração do canal WhatsApp (Cloud API). As credenciais são só de escrita: a API nunca as devolve. */
@Component({
  selector: 'app-whatsapp-dialog',
  imports: [ReactiveFormsModule, MatDialogModule, MatButtonModule, MatFormFieldModule, MatIconModule, MatInputModule, MatSelectModule, TranslocoPipe],
  templateUrl: './whatsapp-dialog.html',
  styleUrl: './whatsapp-dialog.scss'
})
export class WhatsAppDialog {
  private readonly api = inject(ChannelsApi);
  private readonly ref = inject(MatDialogRef<WhatsAppDialog, Channel>);
  protected readonly data = inject<WhatsAppDialogData>(MAT_DIALOG_DATA);

  private readonly channel = this.data.channel;
  protected readonly info = this.channel?.whatsApp ?? null;
  protected readonly saving = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly serverErrors = signal<Record<string, string>>({});
  protected readonly showToken = signal(false);

  protected readonly form = inject(NonNullableFormBuilder).group({
    companyId: [this.channel?.companyId ?? ''],
    agentId: [this.channel?.agentId ?? this.data.agents[0]?.id ?? '', Validators.required],
    name: [this.channel?.name ?? 'WhatsApp', [Validators.required, Validators.maxLength(80)]],
    phoneNumberId: [this.info?.phoneNumberId ?? '', [Validators.required, Validators.pattern(/^[0-9]{5,30}$/)]],
    businessAccountId: [this.info?.businessAccountId ?? '', Validators.pattern(/^[0-9]{5,30}$/)],
    displayPhoneNumber: [this.info?.displayPhoneNumber ?? '', Validators.maxLength(30)],
    accessToken: ['', [Validators.maxLength(2048), Validators.pattern(/^\S*$/)]],
    appSecret: ['', Validators.pattern(/^([A-Za-z0-9]{16,128})?$/)]
  });

  protected agentsFor(companyId: string): AgentListItem[] {
    return this.data.isSuperAdmin && !this.channel && companyId ? this.data.agents.filter((a) => a.companyId === companyId) : this.data.agents;
  }

  save(): void {
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }
    const v = this.form.getRawValue();
    const body: SaveWhatsApp = {
      companyId: this.channel ? null : v.companyId || null,
      agentId: v.agentId,
      name: v.name.trim(),
      phoneNumberId: v.phoneNumberId.trim(),
      businessAccountId: v.businessAccountId.trim() || null,
      displayPhoneNumber: v.displayPhoneNumber.trim() || null,
      accessToken: v.accessToken.trim() || null,
      appSecret: v.appSecret.trim() || null
    };
    this.saving.set(true);
    this.error.set(null);
    (this.channel ? this.api.updateWhatsApp(this.channel.id, body) : this.api.createWhatsApp(body)).subscribe({
      next: (saved) => this.ref.close(saved),
      error: (err) => {
        this.saving.set(false);
        this.serverErrors.set(fieldErrors(err));
        this.error.set(errorMessage(err));
      }
    });
  }
}
