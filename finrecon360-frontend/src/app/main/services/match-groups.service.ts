import { Injectable, inject } from '@angular/core';
import { HttpClient, HttpParams } from '@angular/common/http';
import { Observable } from 'rxjs';
import { environment } from '../../../../environments/environment';

export interface MatchedRecordDetail {
  importedNormalizedRecordId: string;
  sourceType: string;
  transactionDate: string;
  netAmount: number;
  referenceNumber?: string;
  accountCode?: string;
  description?: string;
}

export interface PendingMatchSummary {
  matchGroupId: string;
  matchLevel: string;
  settlementKey: string;
  matchedAmount: number;
  variance: number;
  status: string;
  createdAt: string;
  records: MatchedRecordDetail[];
}

export interface UnmatchedHint {
  hintType: string;
  relatedTransactionId?: string;
  relatedDescription?: string;
  relatedAmount?: number;
  hintMessage: string;
}

export interface UnmatchedQueueItem {
  importedNormalizedRecordId: string;
  sourceType: string;
  matchRule: string;
  amount: number;
  transactionDate: string;
  referenceNumber?: string;
  unmatchedReason: string;
  hint?: UnmatchedHint;
}

export interface RuleSummary {
  matchLevel: string;
  pendingConfirmations: number;
  exceptions: number;
  unmatched: number;
}

export interface MatcherSummary {
  totalPendingConfirmations: number;
  totalExceptions: number;
  totalUnmatched: number;
  rules: RuleSummary[];
}

@Injectable({
  providedIn: 'root'
})
export class MatchGroupsService {
  private http = inject(HttpClient);
  private apiUrl = `${environment.apiUrl}/api/admin/match-groups`;

  getSummary(): Observable<MatcherSummary> {
    return this.http.get<MatcherSummary>(`${this.apiUrl}/summary`);
  }

  getPendingMatches(level?: string): Observable<PendingMatchSummary[]> {
    let params = new HttpParams();
    if (level) {
      params = params.set('level', level);
    }
    return this.http.get<PendingMatchSummary[]>(`${this.apiUrl}/pending`, { params });
  }

  confirmMatch(id: string, note?: string): Observable<{ message: string }> {
    return this.http.post<{ message: string }>(`${this.apiUrl}/${id}/confirm`, { note });
  }

  rejectMatch(id: string, reason: string): Observable<{ message: string }> {
    return this.http.post<{ message: string }>(`${this.apiUrl}/${id}/reject`, { reason });
  }

  getUnmatched(rule?: string, from?: string, to?: string): Observable<UnmatchedQueueItem[]> {
    let params = new HttpParams();
    if (rule) params = params.set('rule', rule);
    if (from) params = params.set('from', from);
    if (to) params = params.set('to', to);
    return this.http.get<UnmatchedQueueItem[]>(`${this.apiUrl}/unmatched`, { params });
  }
}
