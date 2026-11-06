import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';

export interface SystemInfo {
  name: string;
  version: string;
  environment: string;
  database: { online: boolean; pendingMigrations: number };
  aiProviderConfigured: boolean;
  serverTimeUtc: string;
}

@Injectable({ providedIn: 'root' })
export class SystemService {
  private readonly http = inject(HttpClient);

  info(): Observable<SystemInfo> {
    return this.http.get<SystemInfo>('/api/v1/system/info');
  }
}
