import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { Company, CompanyStatus, CompanySummary, PagedResult, SaveCompany } from '../models';

export interface CompanyFilter {
  search?: string;
  status?: CompanyStatus | '';
  page?: number;
  pageSize?: number;
}

@Injectable({ providedIn: 'root' })
export class CompaniesApi {
  private readonly http = inject(HttpClient);
  private readonly base = '/api/v1/companies';

  list(filter: CompanyFilter = {}): Observable<PagedResult<Company>> {
    let params = new HttpParams();
    for (const [key, value] of Object.entries(filter)) {
      if (value !== undefined && value !== null && value !== '') params = params.set(key, String(value));
    }
    return this.http.get<PagedResult<Company>>(this.base, { params });
  }

  get(id: string): Observable<Company> {
    return this.http.get<Company>(`${this.base}/${id}`);
  }

  summary(id: string): Observable<CompanySummary> {
    return this.http.get<CompanySummary>(`${this.base}/${id}/summary`);
  }

  create(body: SaveCompany): Observable<Company> {
    return this.http.post<Company>(this.base, body);
  }

  update(id: string, body: SaveCompany): Observable<Company> {
    return this.http.put<Company>(`${this.base}/${id}`, body);
  }

  setStatus(id: string, status: CompanyStatus): Observable<Company> {
    return this.http.patch<Company>(`${this.base}/${id}/status`, { status });
  }
}
