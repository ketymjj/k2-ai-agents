import { HttpClient, HttpEvent, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { KnowledgeDocument, KnowledgeListItem, KnowledgeSearchHit, KnowledgeSourceType, KnowledgeStatus, PagedResult } from '../models';

export interface KnowledgeFilter {
  companyId?: string;
  agentId?: string;
  generalOnly?: boolean;
  sourceType?: KnowledgeSourceType | '';
  status?: KnowledgeStatus | '';
  search?: string;
  page?: number;
  pageSize?: number;
}

export interface UploadKnowledge {
  file: File;
  agentId: string | null;
  title: string | null;
  companyId: string | null;
}

@Injectable({ providedIn: 'root' })
export class KnowledgeApi {
  private readonly http = inject(HttpClient);
  private readonly base = '/api/v1/knowledge';

  list(filter: KnowledgeFilter = {}): Observable<PagedResult<KnowledgeListItem>> {
    let params = new HttpParams();
    for (const [key, value] of Object.entries(filter)) {
      if (value !== undefined && value !== null && value !== '') params = params.set(key, String(value));
    }
    return this.http.get<PagedResult<KnowledgeListItem>>(this.base, { params });
  }

  get(id: string): Observable<KnowledgeDocument> {
    return this.http.get<KnowledgeDocument>(`${this.base}/${id}`);
  }

  create(body: { title: string; content: string; sourceType: 'Text' | 'Faq'; agentId: string | null; companyId: string | null }): Observable<KnowledgeDocument> {
    return this.http.post<KnowledgeDocument>(this.base, body);
  }

  update(id: string, body: { title: string; content: string; agentId: string | null }): Observable<KnowledgeDocument> {
    return this.http.put<KnowledgeDocument>(`${this.base}/${id}`, body);
  }

  /** Emite eventos de progresso do envio e, ao final, a resposta. */
  upload(data: UploadKnowledge): Observable<HttpEvent<KnowledgeDocument>> {
    const form = new FormData();
    form.append('file', data.file, data.file.name);
    if (data.agentId) form.append('agentId', data.agentId);
    if (data.title) form.append('title', data.title);
    if (data.companyId) form.append('companyId', data.companyId);
    return this.http.post<KnowledgeDocument>(`${this.base}/upload`, form, { reportProgress: true, observe: 'events' });
  }

  setStatus(id: string, status: 'Ready' | 'Archived'): Observable<KnowledgeDocument> {
    return this.http.patch<KnowledgeDocument>(`${this.base}/${id}/status`, { status });
  }

  delete(id: string): Observable<void> {
    return this.http.delete<void>(`${this.base}/${id}`);
  }

  download(id: string): Observable<Blob> {
    return this.http.get(`${this.base}/${id}/download`, { responseType: 'blob' });
  }

  search(query: string, agentId: string | null, companyId: string | null): Observable<KnowledgeSearchHit[]> {
    return this.http.post<KnowledgeSearchHit[]>(`${this.base}/search`, { query, agentId, companyId });
  }
}
