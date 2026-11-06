import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { CreateUser, PagedResult, Role, RoleInfo, UpdateUser, User } from '../models';

export interface UserFilter {
  companyId?: string;
  search?: string;
  role?: Role | '';
  isActive?: boolean | '';
  page?: number;
  pageSize?: number;
}

@Injectable({ providedIn: 'root' })
export class UsersApi {
  private readonly http = inject(HttpClient);
  private readonly base = '/api/v1/users';

  list(filter: UserFilter = {}): Observable<PagedResult<User>> {
    let params = new HttpParams();
    for (const [key, value] of Object.entries(filter)) {
      if (value !== undefined && value !== null && value !== '') params = params.set(key, String(value));
    }
    return this.http.get<PagedResult<User>>(this.base, { params });
  }

  roles(): Observable<RoleInfo[]> {
    return this.http.get<RoleInfo[]>(`${this.base}/roles`);
  }

  create(body: CreateUser): Observable<User> {
    return this.http.post<User>(this.base, body);
  }

  update(id: string, body: UpdateUser): Observable<User> {
    return this.http.put<User>(`${this.base}/${id}`, body);
  }

  setActive(id: string, isActive: boolean): Observable<User> {
    return this.http.patch<User>(`${this.base}/${id}/active`, { isActive });
  }
}
