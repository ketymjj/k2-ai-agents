import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import {
  AIModelOption,
  Agent,
  AgentBehavior,
  AgentListItem,
  AgentStatus,
  AgentTemplate,
  AgentVersion,
  BusinessType,
  Language,
  PagedResult,
  PromptPreview,
  SaveAgent
} from '../models';

export interface AgentFilter {
  companyId?: string;
  search?: string;
  status?: AgentStatus | '';
  businessType?: BusinessType | '';
  page?: number;
  pageSize?: number;
}

@Injectable({ providedIn: 'root' })
export class AgentsApi {
  private readonly http = inject(HttpClient);
  private readonly base = '/api/v1/agents';

  list(filter: AgentFilter = {}): Observable<PagedResult<AgentListItem>> {
    let params = new HttpParams();
    for (const [key, value] of Object.entries(filter)) {
      if (value !== undefined && value !== null && value !== '') params = params.set(key, String(value));
    }
    return this.http.get<PagedResult<AgentListItem>>(this.base, { params });
  }

  get(id: string): Observable<Agent> {
    return this.http.get<Agent>(`${this.base}/${id}`);
  }

  models(): Observable<AIModelOption[]> {
    return this.http.get<AIModelOption[]>(`${this.base}/models`);
  }

  templates(language: Language): Observable<AgentTemplate[]> {
    return this.http.get<AgentTemplate[]>(`${this.base}/templates`, { params: { language } });
  }

  previewPrompt(body: {
    name: string;
    businessType: BusinessType;
    responseLanguage: Language;
    behavior: AgentBehavior;
    companyId: string | null;
  }): Observable<PromptPreview> {
    return this.http.post<PromptPreview>(`${this.base}/preview-prompt`, body);
  }

  create(body: SaveAgent): Observable<Agent> {
    return this.http.post<Agent>(this.base, body);
  }

  update(id: string, body: SaveAgent): Observable<Agent> {
    return this.http.put<Agent>(`${this.base}/${id}`, body);
  }

  duplicate(id: string): Observable<Agent> {
    return this.http.post<Agent>(`${this.base}/${id}/duplicate`, {});
  }

  setStatus(id: string, status: AgentStatus): Observable<Agent> {
    return this.http.patch<Agent>(`${this.base}/${id}/status`, { status });
  }

  versions(id: string): Observable<AgentVersion[]> {
    return this.http.get<AgentVersion[]>(`${this.base}/${id}/versions`);
  }

  restore(id: string, version: number): Observable<Agent> {
    return this.http.post<Agent>(`${this.base}/${id}/versions/${version}/restore`, {});
  }
}
