import { Clipboard } from '@angular/cdk/clipboard';
import { DatePipe } from '@angular/common';
import { Component, OnInit, computed, inject, signal } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatDialog } from '@angular/material/dialog';
import { MatIconModule } from '@angular/material/icon';
import { MatMenuModule } from '@angular/material/menu';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { MatTooltipModule } from '@angular/material/tooltip';
import { RouterLink } from '@angular/router';
import { TranslocoPipe, TranslocoService } from '@jsverse/transloco';
import { filter, forkJoin, of } from 'rxjs';
import { AgentsApi } from '../../../core/api/agents.api';
import { ChannelsApi } from '../../../core/api/channels.api';
import { CompaniesApi } from '../../../core/api/companies.api';
import { AuthService } from '../../../core/auth/auth.service';
import { AGENT_MANAGER_ROLES, AgentListItem, Channel, Company, WhatsAppConnectionResult } from '../../../core/models';
import { WhatsAppDialog, WhatsAppDialogData } from '../whatsapp-dialog/whatsapp-dialog';
import { NotifyService } from '../../../core/notify.service';
import { confirmAction } from '../../../shared/confirm-dialog/confirm-dialog';
import { WebChatDialog, WebChatDialogData } from '../webchat-dialog/webchat-dialog';
import { ModuleGuide } from '../../../shared/module-guide/module-guide';

@Component({
  selector: 'app-channels-list',
  imports: [ModuleGuide, DatePipe, RouterLink, MatButtonModule, MatIconModule, MatMenuModule, MatProgressBarModule, MatTooltipModule, TranslocoPipe],
  templateUrl: './channels-list.html',
  styleUrl: './channels-list.scss'
})
export class ChannelsList implements OnInit {
  private readonly api = inject(ChannelsApi);
  private readonly agentsApi = inject(AgentsApi);
  private readonly companiesApi = inject(CompaniesApi);
  private readonly dialog = inject(MatDialog);
  private readonly notify = inject(NotifyService);
  private readonly transloco = inject(TranslocoService);
  private readonly clipboard = inject(Clipboard);
  protected readonly auth = inject(AuthService);

  protected readonly channels = signal<Channel[]>([]);
  protected readonly agents = signal<AgentListItem[]>([]);
  protected readonly companies = signal<Company[]>([]);
  protected readonly loading = signal(true);
  protected readonly canManage = computed(() => this.auth.hasRole(...AGENT_MANAGER_ROLES));
  /** Credenciais da Meta: só administradores (o backend aplica a mesma regra). */
  protected readonly canManageWhatsApp = computed(() => this.auth.hasRole('SuperAdmin', 'CompanyAdmin'));
  protected readonly hasWhatsApp = computed(() => this.channels().some((c) => c.channelType === 'WhatsApp'));
  protected readonly webhookUrl = ChannelsApi.whatsAppWebhookUrl();
  protected readonly testing = signal<string | null>(null);
  protected readonly testResults = signal<Record<string, WhatsAppConnectionResult>>({});

  ngOnInit(): void {
    forkJoin({
      agents: this.agentsApi.list({ pageSize: 100 }),
      companies: this.auth.isSuperAdmin() ? this.companiesApi.list({ status: 'Active', pageSize: 100 }) : of(null)
    }).subscribe({
      next: ({ agents, companies }) => {
        this.agents.set(agents.items.filter((a) => a.status !== 'Inactive'));
        this.companies.set(companies?.items ?? []);
      },
      error: (e) => this.notify.error(e)
    });
    this.reload();
  }

  reload(): void {
    this.loading.set(true);
    this.api.list().subscribe({
      next: (list) => {
        this.channels.set(list);
        this.loading.set(false);
      },
      error: (err) => {
        this.loading.set(false);
        this.notify.error(err);
      }
    });
  }

  edit(channel: Channel | null = null): void {
    const data: WebChatDialogData = {
      channel,
      agents: channel ? this.agents().filter((a) => a.companyId === channel.companyId) : this.agents(),
      companies: this.companies(),
      isSuperAdmin: this.auth.isSuperAdmin()
    };
    this.dialog
      .open<WebChatDialog, WebChatDialogData, Channel>(WebChatDialog, { data, width: '860px', maxWidth: '95vw' })
      .afterClosed()
      .pipe(filter(Boolean))
      .subscribe(() => {
        this.notify.success(channel ? 'channels.updated' : 'channels.created');
        this.reload();
      });
  }

  editWhatsApp(channel: Channel | null = null): void {
    const data: WhatsAppDialogData = {
      channel,
      agents: channel ? this.agents().filter((a) => a.companyId === channel.companyId) : this.agents(),
      companies: this.companies(),
      isSuperAdmin: this.auth.isSuperAdmin()
    };
    this.dialog
      .open<WhatsAppDialog, WhatsAppDialogData, Channel>(WhatsAppDialog, { data, width: '760px', maxWidth: '95vw' })
      .afterClosed()
      .pipe(filter(Boolean))
      .subscribe(() => {
        this.notify.success(channel ? 'channels.updated' : 'whatsapp.created');
        this.reload();
      });
  }

  testWhatsApp(channel: Channel): void {
    this.testing.set(channel.id);
    this.api.testWhatsApp(channel.id).subscribe({
      next: (result) => {
        this.testing.set(null);
        this.testResults.update((r) => ({ ...r, [channel.id]: result }));
        if (result.ok) this.reload();
      },
      error: (err) => {
        this.testing.set(null);
        this.notify.error(err);
      }
    });
  }

  copyText(text: string): void {
    if (this.clipboard.copy(text)) this.notify.success('learn.copied');
  }

  snippet(channel: Channel): string {
    return channel.publicToken ? ChannelsApi.snippet(channel.publicToken) : '';
  }

  demoUrl(channel: Channel): string {
    return `/widget/demo.html?token=${encodeURIComponent(channel.publicToken ?? '')}`;
  }

  copy(channel: Channel): void {
    if (this.clipboard.copy(this.snippet(channel))) this.notify.success('channels.copied');
  }

  toggle(channel: Channel): void {
    const activating = channel.status !== 'Active';
    this.api.setStatus(channel.id, activating ? 'Active' : 'Inactive').subscribe({
      next: () => {
        this.notify.success(activating ? 'channels.activated' : 'channels.deactivated');
        this.reload();
      },
      error: (err) => this.notify.error(err)
    });
  }

  regenerate(channel: Channel): void {
    confirmAction(this.dialog, {
      title: this.transloco.translate('channels.regenerateTitle'),
      message: this.transloco.translate('channels.regenerateMessage'),
      danger: true
    })
      .pipe(filter(Boolean))
      .subscribe(() =>
        this.api.regenerateToken(channel.id).subscribe({
          next: () => {
            this.notify.success('channels.regenerated');
            this.reload();
          },
          error: (err) => this.notify.error(err)
        })
      );
  }

  remove(channel: Channel): void {
    confirmAction(this.dialog, {
      title: this.transloco.translate('channels.deleteTitle'),
      message: this.transloco.translate('channels.deleteMessage', { name: channel.name }),
      confirmLabel: this.transloco.translate('knowledge.delete'),
      danger: true
    })
      .pipe(filter(Boolean))
      .subscribe(() =>
        this.api.delete(channel.id).subscribe({
          next: () => {
            this.notify.success('channels.deleted');
            this.reload();
          },
          error: (err) => this.notify.error(err)
        })
      );
  }
}
