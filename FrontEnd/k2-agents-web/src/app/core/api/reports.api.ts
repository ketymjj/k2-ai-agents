import { HttpClient, HttpParams, HttpResponse } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable, map } from 'rxjs';
import { ChannelType } from '../models';

export interface ReportFilter {
  companyId?: string;
  from?: string;
  to?: string;
  agentId?: string;
  channel?: ChannelType | '';
  lang?: string;
}

export interface Kpis {
  conversations: number;
  closedConversations: number;
  resolvedByAI: number;
  transferred: number;
  resolutionRate: number | null;
  customerMessages: number;
  aiReplies: number;
  humanReplies: number;
  avgAIResponseMs: number | null;
  newContacts: number;
  newLeads: number;
  appointments: number;
  appointmentsByAgent: number;
  aiCalls: number;
  aiFailures: number;
  simulatedReplies: number;
  inputTokens: number;
  outputTokens: number;
  estimatedCostUsd: number;
}

export interface DailyPoint {
  date: string;
  conversations: number;
  leads: number;
  appointments: number;
  aiCalls: number;
  estimatedCostUsd: number;
}

export interface AgentStats {
  agentId: string;
  name: string;
  conversations: number;
  transferred: number;
  aiReplies: number;
  avgAIResponseMs: number | null;
  estimatedCostUsd: number;
}

export interface Dashboard {
  period: { from: string; to: string; days: number; timeZone: string };
  kpis: Kpis;
  previous: Kpis;
  daily: DailyPoint[];
  byAgent: AgentStats[];
  byChannel: { channel: ChannelType; conversations: number; aiCalls: number; estimatedCostUsd: number }[];
  appointmentsByStatus: { status: string; count: number }[];
  conversationsByStatus: { status: string; count: number }[];
  aiConfigured: boolean;
  demoConversations: number;
}

export interface CostMonth {
  month: string;
  aiCalls: number;
  tokens: number;
  estimatedUsd: number;
  billedUsd: number | null;
  billedNotes: string | null;
  differenceUsd: number | null;
  differencePct: number | null;
}

export interface CostReport {
  months: CostMonth[];
  byCompany: { companyId: string; name: string; aiCalls: number; tokens: number; estimatedUsd: number }[];
  totalEstimatedUsd: number;
  totalBilledUsd: number | null;
  canSeeBilling: boolean;
}

export type ExportType = 'Conversations' | 'Leads' | 'Appointments' | 'AIUsage';

function toParams(filter: object): HttpParams {
  let params = new HttpParams();
  for (const [key, value] of Object.entries(filter)) {
    if (value !== undefined && value !== null && value !== '') params = params.set(key, String(value));
  }
  return params;
}

@Injectable({ providedIn: 'root' })
export class ReportsApi {
  private readonly http = inject(HttpClient);
  private readonly base = '/api/v1/reports';

  dashboard(filter: ReportFilter): Observable<Dashboard> {
    return this.http.get<Dashboard>(`${this.base}/dashboard`, { params: toParams(filter) });
  }

  costs(companyId?: string, from?: string, to?: string): Observable<CostReport> {
    return this.http.get<CostReport>(`${this.base}/costs`, { params: toParams({ companyId, from, to }) });
  }

  saveBilling(month: string, amountUsd: number, notes: string | null): Observable<CostMonth> {
    return this.http.put<CostMonth>(`${this.base}/billing`, { month, amountUsd, notes });
  }

  deleteBilling(month: string): Observable<void> {
    return this.http.delete<void>(`${this.base}/billing/${month}`);
  }

  exportCsv(type: ExportType, filter: ReportFilter): Observable<void> {
    return this.download(`${this.base}/export/${type}`, filter);
  }

  summaryPdf(filter: ReportFilter): Observable<void> {
    return this.download(`${this.base}/summary.pdf`, filter);
  }

  /** Baixa pelo HttpClient (com o token de acesso) e entrega o arquivo ao navegador. */
  private download(url: string, filter: ReportFilter): Observable<void> {
    return this.http.get(url, { params: toParams(filter), responseType: 'blob', observe: 'response' }).pipe(
      map((response: HttpResponse<Blob>) => {
        const disposition = response.headers.get('content-disposition') ?? '';
        const name = /filename\*=UTF-8''([^;]+)/i.exec(disposition)?.[1] ?? /filename="?([^";]+)"?/i.exec(disposition)?.[1] ?? 'k2-relatorio';
        const href = URL.createObjectURL(response.body!);
        const a = document.createElement('a');
        a.href = href;
        a.download = decodeURIComponent(name);
        document.body.appendChild(a);
        a.click();
        a.remove();
        setTimeout(() => URL.revokeObjectURL(href), 1000);
      })
    );
  }
}
