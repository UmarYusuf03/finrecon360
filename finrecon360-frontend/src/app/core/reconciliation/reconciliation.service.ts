import { HttpClient } from '@angular/common/http';
import { Injectable } from '@angular/core';
import { Observable } from 'rxjs';
import { map } from 'rxjs/operators';

import { environment } from '../../../environments/environment';
import {
  BankAccount,
  BankStatementImport,
  BankStatementLine,
  MatchingSummaryResponse,
  MatchGroup,
  ConfirmMatchesResponse,
  PaginatedResponse,
  RunMatchingRequest,
} from './reconciliation.models';

@Injectable({ providedIn: 'root' })
export class ReconciliationService {
  private readonly apiUrl = environment.apiBaseUrl;

  constructor(private http: HttpClient) {}

  getBankAccounts(): Observable<BankAccount[]> {
    return this.http.get<BankAccount[]>(`${this.apiUrl}/api/tenant-admin/bank-accounts`);
  }

  getAvailableImports(bankAccountId: string): Observable<BankStatementImport[]> {
    return this.http
      .get<PaginatedResponse<BankStatementImport>>(
        `${this.apiUrl}/api/tenant-admin/reconciliation/imports`,
        {
          params: {
            bankAccountId,
            pageNumber: 1,
            pageSize: 100,
          },
        },
      )
      .pipe(map((response) => response.items ?? []));
  }

  runAutomatedMatching(importId: string): Observable<MatchingSummaryResponse> {
    const payload: RunMatchingRequest = {
      bankStatementImportId: importId,
    };

    return this.http.post<MatchingSummaryResponse>(
      `${this.apiUrl}/api/tenant-admin/reconciliation/run-automated-matching`,
      payload,
    );
  }

  getProposedMatchGroups(): Observable<MatchGroup[]> {
    return this.http.get<MatchGroup[]>(`${this.apiUrl}/api/tenant-admin/reconciliation/proposed-match-groups`);
  }

  confirmMatches(matchGroupIds: string[]): Observable<ConfirmMatchesResponse> {
    return this.http.post<ConfirmMatchesResponse>(
      `${this.apiUrl}/api/tenant-admin/reconciliation/confirm-matches`,
      { matchGroupIds },
    );
  }

  getExceptions(): Observable<BankStatementLine[]> {
    return this.http.get<BankStatementLine[]>(`${this.apiUrl}/api/tenant-admin/reconciliation/exceptions`);
  }
}
