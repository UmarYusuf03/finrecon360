import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable, of } from 'rxjs';

import { API_BASE_URL, API_ENDPOINTS, USE_MOCK_API } from '../constants/api.constants';
import { BillingSettings, PaymentAlert, PaymentAlertSummary } from './models';

@Injectable({ providedIn: 'root' })
export class PaymentAlertService {
  constructor(private http: HttpClient) {}

  getAlerts(status?: string): Observable<PaymentAlert[]> {
    if (USE_MOCK_API) {
      return of([]);
    }

    const query = status ? `?status=${encodeURIComponent(status)}` : '';
    return this.http.get<PaymentAlert[]>(`${API_BASE_URL}${API_ENDPOINTS.SYSTEM.PAYMENT_ALERTS}${query}`);
  }

  getSummary(): Observable<PaymentAlertSummary> {
    if (USE_MOCK_API) {
      return of({ openCount: 0 });
    }

    return this.http.get<PaymentAlertSummary>(`${API_BASE_URL}${API_ENDPOINTS.SYSTEM.PAYMENT_ALERTS}/summary`);
  }

  acknowledge(id: string): Observable<void> {
    return this.http.post<void>(`${API_BASE_URL}${API_ENDPOINTS.SYSTEM.PAYMENT_ALERTS}/${id}/acknowledge`, {});
  }

  resolve(id: string): Observable<void> {
    return this.http.post<void>(`${API_BASE_URL}${API_ENDPOINTS.SYSTEM.PAYMENT_ALERTS}/${id}/resolve`, {});
  }

  getBillingSettings(): Observable<BillingSettings> {
    if (USE_MOCK_API) {
      return of({ paymentOverdueSuspensionThresholdDays: 7, updatedAt: new Date().toISOString() });
    }

    return this.http.get<BillingSettings>(`${API_BASE_URL}${API_ENDPOINTS.SYSTEM.BILLING_SETTINGS}`);
  }

  updateBillingSettings(thresholdDays: number): Observable<BillingSettings> {
    return this.http.put<BillingSettings>(`${API_BASE_URL}${API_ENDPOINTS.SYSTEM.BILLING_SETTINGS}`, {
      paymentOverdueSuspensionThresholdDays: thresholdDays,
    });
  }
}
