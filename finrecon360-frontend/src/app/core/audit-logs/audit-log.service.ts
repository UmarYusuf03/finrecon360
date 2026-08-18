import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';

import { API_BASE_URL, API_ENDPOINTS } from '../constants/api.constants';
import { ExportFormat } from '../services/export.service';
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

  exportAuditLogs(filters: AuditLogFilters, format: ExportFormat): Observable<Blob> {
    return this.exportForEndpoint(filters, API_ENDPOINTS.SYSTEM.AUDIT_LOGS, format);
  }

  exportTenantAuditLogs(filters: AuditLogFilters, format: ExportFormat): Observable<Blob> {
    return this.exportForEndpoint(filters, API_ENDPOINTS.ADMIN.AUDIT_LOGS, format);
  }

  private getAuditLogsForEndpoint(
    filters: AuditLogFilters,
    endpoint: string,
  ): Observable<AuditLogPage> {
    const params = this.buildFilterParams(filters);
    params.set('page', String(filters.page));
    params.set('pageSize', String(filters.pageSize));

    const query = params.toString();
    const url = `${API_BASE_URL}${endpoint}${query ? `?${query}` : ''}`;
    return this.http.get<AuditLogPage>(url);
  }

  private exportForEndpoint(
    filters: AuditLogFilters,
    endpoint: string,
    format: ExportFormat,
  ): Observable<Blob> {
    const params = this.buildFilterParams(filters);
    params.set('format', format);

    const url = `${API_BASE_URL}${endpoint}/export?${params.toString()}`;
    return this.http.get(url, { responseType: 'blob' });
  }

  private buildFilterParams(filters: AuditLogFilters): URLSearchParams {
    const params = new URLSearchParams();

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

    return params;
  }
}
