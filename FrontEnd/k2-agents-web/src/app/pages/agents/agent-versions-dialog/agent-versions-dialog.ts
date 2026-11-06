import { DatePipe } from '@angular/common';
import { Component, OnInit, inject, signal } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MAT_DIALOG_DATA, MatDialog, MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { TranslocoPipe, TranslocoService } from '@jsverse/transloco';
import { filter, switchMap } from 'rxjs';
import { AgentsApi } from '../../../core/api/agents.api';
import { Agent, AgentListItem, AgentVersion } from '../../../core/models';
import { NotifyService } from '../../../core/notify.service';
import { confirmAction } from '../../../shared/confirm-dialog/confirm-dialog';

export interface AgentVersionsData {
  agent: AgentListItem;
  canRestore: boolean;
}

@Component({
  selector: 'app-agent-versions-dialog',
  imports: [DatePipe, MatDialogModule, MatButtonModule, MatIconModule, MatProgressBarModule, TranslocoPipe],
  templateUrl: './agent-versions-dialog.html',
  styleUrl: './agent-versions-dialog.scss'
})
export class AgentVersionsDialog implements OnInit {
  private readonly api = inject(AgentsApi);
  private readonly dialog = inject(MatDialog);
  private readonly notify = inject(NotifyService);
  private readonly transloco = inject(TranslocoService);
  private readonly ref = inject(MatDialogRef<AgentVersionsDialog, Agent>);
  protected readonly data = inject<AgentVersionsData>(MAT_DIALOG_DATA);

  protected readonly versions = signal<AgentVersion[]>([]);
  protected readonly selected = signal<AgentVersion | null>(null);
  protected readonly loading = signal(true);
  protected readonly restoring = signal(false);

  ngOnInit(): void {
    this.api.versions(this.data.agent.id).subscribe({
      next: (versions) => {
        this.versions.set(versions);
        this.selected.set(versions[0] ?? null);
        this.loading.set(false);
      },
      error: (err) => {
        this.loading.set(false);
        this.notify.error(err);
      }
    });
  }

  restore(version: AgentVersion): void {
    confirmAction(this.dialog, {
      title: this.transloco.translate('agents.restoreTitle', { version: version.versionNumber }),
      message: this.transloco.translate('agents.restoreMessage', { version: version.versionNumber })
    })
      .pipe(
        filter(Boolean),
        switchMap(() => {
          this.restoring.set(true);
          return this.api.restore(this.data.agent.id, version.versionNumber);
        })
      )
      .subscribe({
        next: (agent) => {
          this.notify.success('agents.restored', { version: version.versionNumber });
          this.ref.close(agent);
        },
        error: (err) => {
          this.restoring.set(false);
          this.notify.error(err);
        }
      });
  }
}
