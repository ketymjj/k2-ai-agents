import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { AutomationAction, AutomationCatalog, AutomationRule, AutomationTrigger } from '../models';

export interface SaveAutomation {
  companyId: string | null;
  name: string;
  triggerType: AutomationTrigger;
  actionType: AutomationAction;
  configuration: { title: string | null; message: string | null };
  enabled: boolean;
}

@Injectable({ providedIn: 'root' })
export class AutomationsApi {
  private readonly http = inject(HttpClient);
  private readonly base = '/api/v1/automations';

  catalog(): Observable<AutomationCatalog> {
    return this.http.get<AutomationCatalog>(`${this.base}/catalog`);
  }

  list(companyId?: string): Observable<AutomationRule[]> {
    return this.http.get<AutomationRule[]>(this.base, { params: companyId ? { companyId } : {} });
  }

  create(body: SaveAutomation): Observable<AutomationRule> {
    return this.http.post<AutomationRule>(this.base, body);
  }

  update(id: string, body: SaveAutomation): Observable<AutomationRule> {
    return this.http.put<AutomationRule>(`${this.base}/${id}`, body);
  }

  setEnabled(id: string, enabled: boolean): Observable<AutomationRule> {
    return this.http.patch<AutomationRule>(`${this.base}/${id}/enabled`, { enabled });
  }

  delete(id: string): Observable<void> {
    return this.http.delete<void>(`${this.base}/${id}`);
  }
}
