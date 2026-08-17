import { Injectable } from '@angular/core';
import { HttpClient, HttpParams } from '@angular/common/http';
import { Observable, of } from 'rxjs';

import { API_BASE_URL, API_ENDPOINTS, USE_MOCK_API } from '../constants/api.constants';
import { CashFlowForecast } from './models';

@Injectable({ providedIn: 'root' })
export class CashFlowForecastService {
  constructor(private http: HttpClient) {}

  getForecast(bankAccountId: string | null, horizonDays: number): Observable<CashFlowForecast> {
    if (USE_MOCK_API) {
      return of(this.buildMockForecast(bankAccountId, horizonDays));
    }

    let params = new HttpParams().set('horizonDays', horizonDays);
    if (bankAccountId) {
      params = params.set('bankAccountId', bankAccountId);
    }

    return this.http.get<CashFlowForecast>(`${API_BASE_URL}${API_ENDPOINTS.ADMIN.CASH_FLOW_FORECAST}`, { params });
  }

  private buildMockForecast(bankAccountId: string | null, horizonDays: number): CashFlowForecast {
    const today = new Date();
    const history = Array.from({ length: 30 }, (_, i) => {
      const date = new Date(today);
      date.setDate(date.getDate() - (29 - i));
      return { date: date.toISOString(), netAmount: Math.round((Math.random() - 0.4) * 20000) };
    });

    let cumulative = 0;
    const forecast = Array.from({ length: horizonDays }, (_, i) => {
      const date = new Date(today);
      date.setDate(date.getDate() + i + 1);
      const projectedNetFlow = 1500 + Math.round((Math.random() - 0.5) * 1000);
      cumulative += projectedNetFlow;
      return { date: date.toISOString(), projectedNetFlow, cumulativeNetFlow: cumulative, knownPendingAmount: 0 };
    });

    return {
      bankAccountId,
      bankAccountName: bankAccountId ? 'Mock Bank Account' : 'All active bank accounts',
      generatedAt: today.toISOString(),
      lookbackDays: 90,
      dailyAverageNetFlow: 1500,
      settlementLagDays: 3,
      history,
      forecast,
    };
  }
}
