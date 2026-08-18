import { Injectable } from '@angular/core';
import { HttpClient, HttpParams } from '@angular/common/http';
import { Observable } from 'rxjs';

import { API_BASE_URL } from '../constants/api.constants';
import { ExportFormat } from '../services/export.service';
import {
  BalanceSheetReport,
  GeneralLedgerReport,
  IncomeStatementReport,
  TrialBalanceReport,
} from './models';

@Injectable({ providedIn: 'root' })
export class FinancialReportsService {
  private readonly baseUrl = `${API_BASE_URL}/api/admin/financial-reports`;

  constructor(private http: HttpClient) {}

  getGeneralLedger(fromUtc: string, toUtc: string): Observable<GeneralLedgerReport> {
    const params = new HttpParams().set('fromUtc', fromUtc).set('toUtc', toUtc);
    return this.http.get<GeneralLedgerReport>(`${this.baseUrl}/general-ledger`, { params });
  }

  exportGeneralLedger(fromUtc: string, toUtc: string, format: ExportFormat): Observable<Blob> {
    const params = new HttpParams().set('fromUtc', fromUtc).set('toUtc', toUtc).set('format', format);
    return this.http.get(`${this.baseUrl}/general-ledger/export`, { params, responseType: 'blob' });
  }

  getTrialBalance(asOfUtc: string): Observable<TrialBalanceReport> {
    const params = new HttpParams().set('asOfUtc', asOfUtc);
    return this.http.get<TrialBalanceReport>(`${this.baseUrl}/trial-balance`, { params });
  }

  exportTrialBalance(asOfUtc: string, format: ExportFormat): Observable<Blob> {
    const params = new HttpParams().set('asOfUtc', asOfUtc).set('format', format);
    return this.http.get(`${this.baseUrl}/trial-balance/export`, { params, responseType: 'blob' });
  }

  getIncomeStatement(fromUtc: string, toUtc: string): Observable<IncomeStatementReport> {
    const params = new HttpParams().set('fromUtc', fromUtc).set('toUtc', toUtc);
    return this.http.get<IncomeStatementReport>(`${this.baseUrl}/income-statement`, { params });
  }

  exportIncomeStatement(fromUtc: string, toUtc: string, format: ExportFormat): Observable<Blob> {
    const params = new HttpParams().set('fromUtc', fromUtc).set('toUtc', toUtc).set('format', format);
    return this.http.get(`${this.baseUrl}/income-statement/export`, { params, responseType: 'blob' });
  }

  getBalanceSheet(asOfUtc: string): Observable<BalanceSheetReport> {
    const params = new HttpParams().set('asOfUtc', asOfUtc);
    return this.http.get<BalanceSheetReport>(`${this.baseUrl}/balance-sheet`, { params });
  }

  exportBalanceSheet(asOfUtc: string, format: ExportFormat): Observable<Blob> {
    const params = new HttpParams().set('asOfUtc', asOfUtc).set('format', format);
    return this.http.get(`${this.baseUrl}/balance-sheet/export`, { params, responseType: 'blob' });
  }
}
