import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';

import { API_BASE_URL, API_ENDPOINTS } from '../constants/api.constants';
import { AuditLogFilters, AuditLogPage } from './models';

@Injectable({ providedIn: 'root' })
export class AuditLogService {
  constructor(private http: HttpClient) {}

  getAuditLogs(filters: AuditLogFilters): Observable<AuditLogPage> {
    return this.getAuditLogsForEndpoint(filters, API_ENDPOINTS.SYSTEM.AUDIT_LOGS);
  }

  getTenantAuditLogs(filters: AuditLogFilters): Observable<AuditLogPage> {
    return this.getAuditLogsForEndpoint(filters, API_ENDPOINTS.ADMIN.AUDIT_LOGS);
  }

  private getAuditLogsForEndpoint(
    filters: AuditLogFilters,
    endpoint: string,
  ): Observable<AuditLogPage> {
    const params = new URLSearchParams();
    params.set('page', String(filters.page));
    params.set('pageSize', String(filters.pageSize));

    if (filters.action?.trim()) {
      params.set('action', filters.action.trim());
    }

    if (filters.entity?.trim()) {
      params.set('entity', filters.entity.trim());
    }

    if (filters.userId?.trim()) {
      params.set('userId', filters.userId.trim());
    }

    if (filters.fromUtc?.trim()) {
      params.set('fromUtc', filters.fromUtc.trim());
    }

    if (filters.toUtc?.trim()) {
      params.set('toUtc', filters.toUtc.trim());
    }

    if (filters.search?.trim()) {
      params.set('search', filters.search.trim());
    }

    const query = params.toString();
    const url = `${API_BASE_URL}${endpoint}${query ? `?${query}` : ''}`;
    return this.http.get<AuditLogPage>(url);
  }
}
