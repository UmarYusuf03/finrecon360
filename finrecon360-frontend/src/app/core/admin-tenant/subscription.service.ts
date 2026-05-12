import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';

import { API_BASE_URL, API_ENDPOINTS } from '../constants/api.constants';
import {
  SubscriptionCheckoutResponse,
  SubscriptionOverview,
  SubscriptionPlan,
} from './models';

@Injectable({ providedIn: 'root' })
export class SubscriptionService {
  constructor(private http: HttpClient) {}

  getTenantSubscriptionOverview(): Observable<SubscriptionOverview> {
    return this.http.get<SubscriptionOverview>(`${API_BASE_URL}${API_ENDPOINTS.ADMIN.SUBSCRIPTION}`);
  }

  createTenantCheckout(planId: string): Observable<SubscriptionCheckoutResponse> {
    return this.http.post<SubscriptionCheckoutResponse>(
      `${API_BASE_URL}${API_ENDPOINTS.ADMIN.SUBSCRIPTION}/checkout`,
      { planId },
    );
  }

  createSystemTenantCheckout(tenantId: string, planId: string): Observable<SubscriptionCheckoutResponse> {
    return this.http.post<SubscriptionCheckoutResponse>(
      `${API_BASE_URL}${API_ENDPOINTS.SYSTEM.TENANT_SUBSCRIPTION(tenantId)}/checkout`,
      { planId },
    );
  }

  getActivePlans(): Observable<SubscriptionPlan[]> {
    return this.http.get<SubscriptionPlan[]>(`${API_BASE_URL}${API_ENDPOINTS.SYSTEM.PLANS}`);
  }
}