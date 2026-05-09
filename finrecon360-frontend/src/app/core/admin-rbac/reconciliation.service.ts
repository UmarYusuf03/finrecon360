import { Injectable } from '@angular/core';
import { HttpClient, HttpParams } from '@angular/common/http';
import { Observable } from 'rxjs';

import { API_BASE_URL } from '../constants/api.constants';
import {
  AttachSettlementIdRequest,
  JournalEntry,
  PostJournalRequest,
  ReconciliationEvent,
  ReconciliationMatchGroup,
  WaitingRecord,
} from './models';

@Injectable({
  providedIn: 'root',
})
export class ReconciliationService {
  private readonly baseUrl = `${API_BASE_URL}/api/admin/reconciliation`;

  constructor(private http: HttpClient) {}

  // ─── Match Groups ─────────────────────────────────────────────────────────

  getMatchGroups(filters: {
    matchLevel?: string;
    isConfirmed?: boolean;
    isJournalPosted?: boolean;
  } = {}): Observable<ReconciliationMatchGroup[]> {
    let params = new HttpParams();
    if (filters.matchLevel != null) params = params.set('matchLevel', filters.matchLevel);
    if (filters.isConfirmed != null) params = params.set('isConfirmed', String(filters.isConfirmed));
    if (filters.isJournalPosted != null) params = params.set('isJournalPosted', String(filters.isJournalPosted));

    return this.http.get<ReconciliationMatchGroup[]>(`${this.baseUrl}/match-groups`, { params });
  }

  getMatchGroup(id: string): Observable<ReconciliationMatchGroup> {
    return this.http.get<ReconciliationMatchGroup>(`${this.baseUrl}/match-groups/${id}`);
  }

  /**
   * Human-confirmation gate. Marks a match group as confirmed, unlocking journal posting.
   * Idempotent on the backend.
   */
  confirmMatchGroup(id: string): Observable<ReconciliationMatchGroup> {
    return this.http.post<ReconciliationMatchGroup>(
      `${this.baseUrl}/match-groups/${id}/confirm`,
      {},
    );
  }

  // ─── Events ──────────────────────────────────────────────────────────────

  getEvents(filters: {
    importBatchId?: string;
    stage?: string;
    sourceType?: string;
    status?: string;
  } = {}): Observable<ReconciliationEvent[]> {
    let params = new HttpParams();
    if (filters.importBatchId) params = params.set('importBatchId', filters.importBatchId);
    if (filters.stage) params = params.set('stage', filters.stage);
    if (filters.sourceType) params = params.set('sourceType', filters.sourceType);
    if (filters.status) params = params.set('status', filters.status);

    return this.http.get<ReconciliationEvent[]>(`${this.baseUrl}/events`, { params });
  }

  // ─── Waiting Queue ────────────────────────────────────────────────────────

  getWaitingQueue(): Observable<WaitingRecord[]> {
    return this.http.get<WaitingRecord[]>(`${this.baseUrl}/waiting-queue`);
  }

  attachSettlementId(recordId: string, request: AttachSettlementIdRequest): Observable<void> {
    return this.http.patch<void>(
      `${this.baseUrl}/records/${recordId}/settlement-id`,
      request,
    );
  }

  // ─── Journal Entries ──────────────────────────────────────────────────────

  getJournalEntries(filters: {
    transactionId?: string;
    matchGroupId?: string;
  } = {}): Observable<JournalEntry[]> {
    let params = new HttpParams();
    if (filters.transactionId) params = params.set('transactionId', filters.transactionId);
    if (filters.matchGroupId) params = params.set('matchGroupId', filters.matchGroupId);

    return this.http.get<JournalEntry[]>(`${this.baseUrl}/journal-entries`, { params });
  }

  postJournalFromTransaction(transactionId: string, request: PostJournalRequest): Observable<JournalEntry> {
    return this.http.post<JournalEntry>(
      `${this.baseUrl}/journal-entries/post-from-transaction/${transactionId}`,
      request,
    );
  }

  postJournalFromMatchGroup(matchGroupId: string, request: PostJournalRequest): Observable<JournalEntry> {
    return this.http.post<JournalEntry>(
      `${this.baseUrl}/match-groups/${matchGroupId}/post-journal`,
      request,
    );
  }
}
