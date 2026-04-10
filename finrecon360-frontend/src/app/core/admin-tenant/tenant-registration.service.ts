import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { map } from 'rxjs/operators';

import { API_BASE_URL, API_ENDPOINTS } from '../constants/api.constants';
import { TenantRegistrationApprovalResult, TenantRegistrationSummary } from './models';
import { PagedResult } from '../admin-rbac/models';

@Injectable({ providedIn: 'root' })
export class TenantRegistrationService {
  constructor(private http: HttpClient) {}

  getRegistrations(status?: string): Observable<TenantRegistrationSummary[]> {
    const query = status ? `?status=${encodeURIComponent(status)}&page=1&pageSize=100` : '?page=1&pageSize=100';
    return this.http
      .get<PagedResult<TenantRegistrationSummary>>(`${API_BASE_URL}${API_ENDPOINTS.SYSTEM.TENANT_REGISTRATIONS}${query}`)
      .pipe(map((result) => result.items));
  }

  approve(id: string, note?: string): Observable<TenantRegistrationApprovalResult> {
    return this.http.post<TenantRegistrationApprovalResult>(
      `${API_BASE_URL}${API_ENDPOINTS.SYSTEM.TENANT_REGISTRATIONS}/${id}/approve`,
      { note }
    );
  }

  reject(id: string, note?: string): Observable<void> {
    return this.http.post<void>(
      `${API_BASE_URL}${API_ENDPOINTS.SYSTEM.TENANT_REGISTRATIONS}/${id}/reject`,
      { note }
    );
  }
}
