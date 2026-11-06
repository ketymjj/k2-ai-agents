import { Injectable, inject } from '@angular/core';
import { MatDialog } from '@angular/material/dialog';
import { TranslocoService } from '@jsverse/transloco';
import { EMPTY, Observable, filter, switchMap, tap } from 'rxjs';
import { AgentsApi } from '../../core/api/agents.api';
import { Agent, AgentListItem } from '../../core/models';
import { NotifyService } from '../../core/notify.service';
import { confirmAction } from '../../shared/confirm-dialog/confirm-dialog';
import { AgentVersionsDialog, AgentVersionsData } from './agent-versions-dialog/agent-versions-dialog';

/** Ações de agente compartilhadas entre a lista e o editor. Cada uma emite o agente atualizado. */
@Injectable({ providedIn: 'root' })
export class AgentActions {
  private readonly api = inject(AgentsApi);
  private readonly dialog = inject(MatDialog);
  private readonly notify = inject(NotifyService);
  private readonly transloco = inject(TranslocoService);

  toggleStatus(agent: AgentListItem): Observable<Agent> {
    const activating = agent.status !== 'Active';
    const t = (key: string) => this.transloco.translate(key, { name: agent.name });
    return confirmAction(this.dialog, {
      title: t(activating ? 'agents.confirmActivateTitle' : 'agents.confirmDeactivateTitle'),
      message: t(activating ? 'agents.confirmActivate' : 'agents.confirmDeactivate'),
      confirmLabel: t(activating ? 'common.activate' : 'common.deactivate'),
      danger: !activating
    }).pipe(
      filter(Boolean),
      switchMap(() => this.api.setStatus(agent.id, activating ? 'Active' : 'Inactive')),
      tap({
        next: () => this.notify.success(activating ? 'agents.activated' : 'agents.deactivated'),
        error: (err) => this.notify.error(err)
      })
    );
  }

  duplicate(agent: AgentListItem): Observable<Agent> {
    return this.api.duplicate(agent.id).pipe(
      tap({
        next: () => this.notify.success('agents.duplicated', { name: agent.name }),
        error: (err) => this.notify.error(err)
      })
    );
  }

  /** Abre o histórico; emite o agente quando uma versão é restaurada. */
  versions(agent: AgentListItem, canRestore: boolean): Observable<Agent> {
    return this.dialog
      .open<AgentVersionsDialog, AgentVersionsData, Agent>(AgentVersionsDialog, {
        data: { agent, canRestore },
        width: '960px',
        maxWidth: '95vw'
      })
      .afterClosed()
      .pipe(switchMap((restored) => (restored ? [restored] : EMPTY)));
  }
}
