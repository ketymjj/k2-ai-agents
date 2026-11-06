import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { Channel, SaveWebChat, SaveWhatsApp, WhatsAppConnectionResult } from '../models';

@Injectable({ providedIn: 'root' })
export class ChannelsApi {
  private readonly http = inject(HttpClient);
  private readonly base = '/api/v1/channels';

  list(companyId?: string): Observable<Channel[]> {
    return this.http.get<Channel[]>(this.base, { params: companyId ? { companyId } : {} });
  }

  createWebChat(body: SaveWebChat): Observable<Channel> {
    return this.http.post<Channel>(`${this.base}/webchat`, body);
  }

  updateWebChat(id: string, body: SaveWebChat): Observable<Channel> {
    return this.http.put<Channel>(`${this.base}/${id}/webchat`, body);
  }

  createWhatsApp(body: SaveWhatsApp): Observable<Channel> {
    return this.http.post<Channel>(`${this.base}/whatsapp`, body);
  }

  updateWhatsApp(id: string, body: SaveWhatsApp): Observable<Channel> {
    return this.http.put<Channel>(`${this.base}/${id}/whatsapp`, body);
  }

  /** Chamada real à Graph API da Meta com as credenciais salvas no servidor. */
  testWhatsApp(id: string): Observable<WhatsAppConnectionResult> {
    return this.http.post<WhatsAppConnectionResult>(`${this.base}/${id}/whatsapp/test`, {});
  }

  /** URL a cadastrar no painel da Meta (precisa ser HTTPS público em produção). */
  static whatsAppWebhookUrl(): string {
    return `${location.origin}/api/v1/webhooks/whatsapp`;
  }

  setStatus(id: string, status: 'Active' | 'Inactive'): Observable<Channel> {
    return this.http.patch<Channel>(`${this.base}/${id}/status`, { status });
  }

  regenerateToken(id: string): Observable<Channel> {
    return this.http.post<Channel>(`${this.base}/${id}/regenerate-token`, {});
  }

  delete(id: string): Observable<void> {
    return this.http.delete<void>(`${this.base}/${id}`);
  }

  /** Código de instalação do widget para colar no site. */
  static snippet(token: string): string {
    return `<script src="${location.origin}/widget/k2-webchat.js" data-token="${token}" async></script>`;
  }
}
