import { Injectable } from '@angular/core';
import { HttpClient, HttpParams } from '@angular/common/http';
import { Observable } from 'rxjs';

import { API_BASE_URL } from '../constants/api.constants';
import { ExportFormat } from '../services/export.service';
import { ReconciliationTrendReport } from './models';

@Injectable({ providedIn: 'root' })
export class ReconciliationReportsService {
  private readonly baseUrl = `${API_BASE_URL}/api/admin/reconciliation-reports`;

  constructor(private http: HttpClient) {}

  getTrend(fromUtc: string, toUtc: string, level: string | null): Observable<ReconciliationTrendReport> {
    let params = new HttpParams().set('fromUtc', fromUtc).set('toUtc', toUtc);
    if (level) {
      params = params.set('level', level);
    }
    return this.http.get<ReconciliationTrendReport>(`${this.baseUrl}/trend`, { params });
  }

  exportTrend(fromUtc: string, toUtc: string, level: string | null, format: ExportFormat): Observable<Blob> {
    let params = new HttpParams().set('fromUtc', fromUtc).set('toUtc', toUtc).set('format', format);
    if (level) {
      params = params.set('level', level);
    }
    return this.http.get(`${this.baseUrl}/trend/export`, { params, responseType: 'blob' });
  }
}
