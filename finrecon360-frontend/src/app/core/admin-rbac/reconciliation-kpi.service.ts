import { Injectable } from '@angular/core';
import { HttpClient, HttpParams } from '@angular/common/http';
import { Observable } from 'rxjs';

import { API_BASE_URL } from '../constants/api.constants';

// ─── Models ─────────────────────────────────────────────────────────────────

export interface ReportSnapshotDto {
  reportSnapshotId: string;
  snapshotDate: string;
  totalUnmatchedCardCashouts: number;
  pendingExceptions: number;
  totalJournalReady: number;
  reconciliationCompletionPercentage: number;
  totalMatchGroupsConfirmed: number;
  totalFeeAdjustments: number;
  createdAt: string;
}

// ─── Service ────────────────────────────────────────────────────────────────

@Injectable({
  providedIn: 'root',
})
export class ReconciliationKpiService {
  private readonly baseUrl = `${API_BASE_URL}/api/admin/dashboard/reconciliation-kpis`;

  constructor(private http: HttpClient) {}

  /**
   * Returns the most recent KPI snapshot for this tenant.
   * Powers the top-level summary cards.
   */
  getLatestSnapshot(): Observable<ReportSnapshotDto> {
    return this.http.get<ReportSnapshotDto>(`${this.baseUrl}/latest`);
  }

  /**
   * Returns KPI snapshots within the specified date range, ordered ascending.
   * Powers the historical trend chart.
   */
  getSnapshotHistory(startDate: string, endDate: string): Observable<ReportSnapshotDto[]> {
    const params = new HttpParams()
      .set('startDate', startDate)
      .set('endDate', endDate);
    return this.http.get<ReportSnapshotDto[]>(`${this.baseUrl}/history`, { params });
  }
}
