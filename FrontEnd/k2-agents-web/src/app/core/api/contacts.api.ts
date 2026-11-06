import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { Contact, ContactDetail, ContactStatus, Funnel, InterestLevel, PagedResult, SaveContact } from '../models';

export interface ContactFilter {
  companyId?: string;
  status?: ContactStatus | '';
  isLead?: boolean | '';
  interest?: InterestLevel | '';
  search?: string;
  page?: number;
  pageSize?: number;
}

@Injectable({ providedIn: 'root' })
export class ContactsApi {
  private readonly http = inject(HttpClient);
  private readonly base = '/api/v1/contacts';

  list(filter: ContactFilter = {}): Observable<PagedResult<Contact>> {
    let params = new HttpParams();
    for (const [key, value] of Object.entries(filter)) {
      if (value !== undefined && value !== null && value !== '') params = params.set(key, String(value));
    }
    return this.http.get<PagedResult<Contact>>(this.base, { params });
  }

  funnel(companyId?: string): Observable<Funnel> {
    return this.http.get<Funnel>(`${this.base}/funnel`, { params: companyId ? { companyId } : {} });
  }

  get(id: string): Observable<ContactDetail> {
    return this.http.get<ContactDetail>(`${this.base}/${id}`);
  }

  create(body: SaveContact): Observable<Contact> {
    return this.http.post<Contact>(this.base, body);
  }

  update(id: string, body: SaveContact): Observable<Contact> {
    return this.http.put<Contact>(`${this.base}/${id}`, body);
  }

  setStatus(id: string, status: ContactStatus): Observable<Contact> {
    return this.http.patch<Contact>(`${this.base}/${id}/status`, { status });
  }

  anonymize(id: string): Observable<Contact> {
    return this.http.post<Contact>(`${this.base}/${id}/anonymize`, {});
  }

  /** (11) 98888-1234 a partir dos dígitos armazenados. */
  static formatPhone(digits: string | null | undefined): string {
    if (!digits) return '—';
    const d = digits.startsWith('55') && digits.length >= 12 ? digits.slice(2) : digits;
    if (d.length === 11) return `(${d.slice(0, 2)}) ${d.slice(2, 7)}-${d.slice(7)}`;
    if (d.length === 10) return `(${d.slice(0, 2)}) ${d.slice(2, 6)}-${d.slice(6)}`;
    return digits;
  }
}
