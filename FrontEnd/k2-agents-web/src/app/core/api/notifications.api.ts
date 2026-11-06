import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { AppNotification } from '../models';

@Injectable({ providedIn: 'root' })
export class NotificationsApi {
  private readonly http = inject(HttpClient);
  private readonly base = '/api/v1/notifications';

  list(take = 20): Observable<{ items: AppNotification[]; unread: number }> {
    return this.http.get<{ items: AppNotification[]; unread: number }>(this.base, { params: { take } });
  }

  markRead(id: string): Observable<void> {
    return this.http.post<void>(`${this.base}/${id}/read`, {});
  }

  markAllRead(): Observable<void> {
    return this.http.post<void>(`${this.base}/read-all`, {});
  }
}
