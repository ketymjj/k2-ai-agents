import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { ChannelType, ChatMessage, ConversationDetail, ConversationEvent, ConversationStatus, ConversationSummary, PagedResult } from '../models';

export interface ConversationFilter {
  companyId?: string;
  agentId?: string;
  mine?: boolean;
  status?: ConversationStatus | '';
  channel?: ChannelType | '';
  search?: string;
  page?: number;
  pageSize?: number;
}

@Injectable({ providedIn: 'root' })
export class ConversationsApi {
  private readonly http = inject(HttpClient);
  private readonly base = '/api/v1/conversations';

  list(filter: ConversationFilter = {}): Observable<PagedResult<ConversationSummary>> {
    let params = new HttpParams();
    for (const [key, value] of Object.entries(filter)) {
      if (value !== undefined && value !== null && value !== '') params = params.set(key, String(value));
    }
    return this.http.get<PagedResult<ConversationSummary>>(this.base, { params });
  }

  get(id: string): Observable<ConversationDetail> {
    return this.http.get<ConversationDetail>(`${this.base}/${id}`);
  }

  close(id: string): Observable<ConversationSummary> {
    return this.http.post<ConversationSummary>(`${this.base}/${id}/close`, {});
  }

  reopen(id: string): Observable<ConversationSummary> {
    return this.http.post<ConversationSummary>(`${this.base}/${id}/reopen`, {});
  }

  transfer(id: string): Observable<ConversationSummary> {
    return this.http.post<ConversationSummary>(`${this.base}/${id}/transfer`, {});
  }

  claim(id: string): Observable<ConversationSummary> {
    return this.http.post<ConversationSummary>(`${this.base}/${id}/claim`, {});
  }

  returnToAi(id: string): Observable<ConversationSummary> {
    return this.http.post<ConversationSummary>(`${this.base}/${id}/return-to-ai`, {});
  }

  reply(id: string, content: string): Observable<ChatMessage> {
    return this.http.post<ChatMessage>(`${this.base}/${id}/messages`, { content });
  }

  events(id: string): Observable<ConversationEvent[]> {
    return this.http.get<ConversationEvent[]>(`${this.base}/${id}/events`);
  }

  linkContact(id: string, body: { contactId?: string; name?: string; email?: string | null; phone?: string | null }): Observable<ConversationDetail> {
    return this.http.post<ConversationDetail>(`${this.base}/${id}/contact`, body);
  }
}
