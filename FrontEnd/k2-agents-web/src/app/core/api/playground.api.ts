import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { PlaygroundSession, PlaygroundSessionSummary, PlaygroundStatus, SendMessageResult } from '../models';

@Injectable({ providedIn: 'root' })
export class PlaygroundApi {
  private readonly http = inject(HttpClient);
  private readonly base = '/api/v1/playground';

  status(agentId?: string): Observable<PlaygroundStatus> {
    return this.http.get<PlaygroundStatus>(`${this.base}/status`, { params: agentId ? { agentId } : {} });
  }

  sessions(agentId: string): Observable<PlaygroundSessionSummary[]> {
    return this.http.get<PlaygroundSessionSummary[]>(`${this.base}/sessions`, { params: { agentId } });
  }

  start(agentId: string): Observable<PlaygroundSession> {
    return this.http.post<PlaygroundSession>(`${this.base}/sessions`, { agentId });
  }

  get(id: string): Observable<PlaygroundSession> {
    return this.http.get<PlaygroundSession>(`${this.base}/sessions/${id}`);
  }

  send(id: string, content: string, simulate: boolean): Observable<SendMessageResult> {
    return this.http.post<SendMessageResult>(`${this.base}/sessions/${id}/messages`, { content, simulate });
  }

  close(id: string): Observable<void> {
    return this.http.delete<void>(`${this.base}/sessions/${id}`);
  }
}
