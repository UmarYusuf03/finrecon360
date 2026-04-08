import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { map } from 'rxjs/operators';

import { API_BASE_URL, API_ENDPOINTS } from '../constants/api.constants';
import { PagedResult } from '../admin-rbac/models';
import { TenantDetail, TenantSummary } from './models';

@Injectable({ providedIn: 'root' })
export class TenantService {
  constructor(private http: HttpClient) {}

  getTenants(status?: string): Observable<TenantSummary[]> {
    const query = status ? `?status=${encodeURIComponent(status)}&page=1&pageSize=100` : '?page=1&pageSize=100';
    return this.http
      .get<PagedResult<TenantSummary>>(`${API_BASE_URL}${API_ENDPOINTS.SYSTEM.TENANTS}${query}`)
      .pipe(map((result) => result.items));
  }

  getTenant(id: string): Observable<TenantDetail> {
    return this.http.get<TenantDetail>(`${API_BASE_URL}${API_ENDPOINTS.SYSTEM.TENANTS}/${id}`);
  }

  updateAdmins(id: string, emails: string[]): Observable<void> {
    return this.http.put<void>(`${API_BASE_URL}${API_ENDPOINTS.SYSTEM.TENANTS}/${id}/admins`, { emails });
  }

  suspend(id: string, reason: string): Observable<void> {
    return this.http.post<void>(`${API_BASE_URL}${API_ENDPOINTS.SYSTEM.TENANTS}/${id}/suspend`, { reason });
  }

  ban(id: string, reason: string): Observable<void> {
    return this.http.post<void>(`${API_BASE_URL}${API_ENDPOINTS.SYSTEM.TENANTS}/${id}/ban`, { reason });
  }

  reinstate(id: string): Observable<void> {
    return this.http.post<void>(`${API_BASE_URL}${API_ENDPOINTS.SYSTEM.TENANTS}/${id}/reinstate`, {});
  }
}
