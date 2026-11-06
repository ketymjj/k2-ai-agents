import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { Appointment, AppointmentStatus, Availability, SchedulingSettings } from '../models';

export interface AppointmentFilter {
  companyId?: string;
  from?: string;
  to?: string;
  status?: AppointmentStatus | '';
  contactId?: string;
}

@Injectable({ providedIn: 'root' })
export class AppointmentsApi {
  private readonly http = inject(HttpClient);
  private readonly base = '/api/v1/appointments';

  list(filter: AppointmentFilter = {}): Observable<Appointment[]> {
    let params = new HttpParams();
    for (const [key, value] of Object.entries(filter)) {
      if (value !== undefined && value !== null && value !== '') params = params.set(key, String(value));
    }
    return this.http.get<Appointment[]>(this.base, { params });
  }

  availability(date: string, service: string, excludeId?: string, companyId?: string): Observable<Availability> {
    let params = new HttpParams().set('date', date).set('service', service);
    if (excludeId) params = params.set('excludeId', excludeId);
    if (companyId) params = params.set('companyId', companyId);
    return this.http.get<Availability>(`${this.base}/availability`, { params });
  }

  settings(companyId?: string): Observable<SchedulingSettings> {
    return this.http.get<SchedulingSettings>(`${this.base}/settings`, { params: companyId ? { companyId } : {} });
  }

  saveSettings(settings: SchedulingSettings, companyId?: string): Observable<SchedulingSettings> {
    return this.http.put<SchedulingSettings>(`${this.base}/settings`, settings, { params: companyId ? { companyId } : {} });
  }

  create(body: {
    companyId: string | null;
    contactId: string | null;
    serviceName: string;
    startDateTime: string;
    durationMinutes: number | null;
    notes: string | null;
  }): Observable<Appointment> {
    return this.http.post<Appointment>(this.base, body);
  }

  reschedule(id: string, startDateTime: string): Observable<Appointment> {
    return this.http.post<Appointment>(`${this.base}/${id}/reschedule`, { startDateTime });
  }

  setStatus(id: string, status: AppointmentStatus): Observable<Appointment> {
    return this.http.patch<Appointment>(`${this.base}/${id}/status`, { status });
  }
}
