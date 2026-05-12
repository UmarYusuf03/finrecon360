import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';

import { API_BASE_URL, API_ENDPOINTS, USE_MOCK_API } from '../constants/api.constants';
import {
  SubscriptionCheckoutResponse,
  SubscriptionOverview,
  SubscriptionPlan,
} from '../admin-tenant/models';

/**
 * WHY: This service provides tenant users the ability to view and manage their subscription
 * directly from their profile page. It uses the /api/me/subscription endpoints which
 * have specific profile-scoped permissions (PROFILE.SUBSCRIPTION.VIEW/CHANGE).
 */
@Injectable({ providedIn: 'root' })
export class ProfileSubscriptionService {
  constructor(private http: HttpClient) {}

  /**
   * Get the current user's subscription overview including current plan and available plans.
   * Requires PROFILE.SUBSCRIPTION.VIEW permission.
   */
  getProfileSubscriptionOverview(): Observable<SubscriptionOverview> {
    if (USE_MOCK_API) {
      // Return mock data for development
      return new Observable(subscriber => {
        const mockData: SubscriptionOverview = {
          currentSubscription: {
            subscriptionId: 'mock-sub-1',
            planCode: 'PRO',
            planName: 'Professional Plan',
            status: 'Active',
            periodStart: new Date(Date.now() - 15 * 24 * 60 * 60 * 1000).toISOString(),
            periodEnd: new Date(Date.now() + 15 * 24 * 60 * 60 * 1000).toISOString(),
          },
          availablePlans: [
            {
              id: 'plan-basic',
              code: 'BASIC',
              name: 'Basic Plan',
              priceCents: 9900,
              currency: 'LKR',
              durationDays: 30,
              maxUsers: 5,
              maxAccounts: 3,
            },
            {
              id: 'plan-pro',
              code: 'PRO',
              name: 'Professional Plan',
              priceCents: 19900,
              currency: 'LKR',
              durationDays: 30,
              maxUsers: 15,
              maxAccounts: 10,
            },
            {
              id: 'plan-enterprise',
              code: 'ENTERPRISE',
              name: 'Enterprise Plan',
              priceCents: 49900,
              currency: 'LKR',
              durationDays: 30,
              maxUsers: 50,
              maxAccounts: 25,
            },
          ],
        };
        subscriber.next(mockData);
        subscriber.complete();
      });
    }

    return this.http.get<SubscriptionOverview>(`${API_BASE_URL}${API_ENDPOINTS.ME_SUBSCRIPTION}`);
  }

  /**
   * Create a checkout session for changing to a new subscription plan.
   * Requires PROFILE.SUBSCRIPTION.CHANGE permission.
   */
  createProfileCheckout(planId: string): Observable<SubscriptionCheckoutResponse> {
    if (USE_MOCK_API) {
      return new Observable(subscriber => {
        subscriber.next({
          subscriptionId: 'mock-sub-new',
          checkoutUrl: 'https://example.com/checkout',
        });
        subscriber.complete();
      });
    }

    return this.http.post<SubscriptionCheckoutResponse>(
      `${API_BASE_URL}${API_ENDPOINTS.ME_SUBSCRIPTION}/checkout`,
      { planId }
    );
  }

  /**
   * Calculate remaining days for a subscription period end date.
   */
  calculateDaysRemaining(periodEnd: string | null | undefined): number {
    if (!periodEnd) {
      return 0;
    }

    const end = new Date(periodEnd);
    const now = new Date();
    const diffTime = end.getTime() - now.getTime();
    const diffDays = Math.ceil(diffTime / (1000 * 60 * 60 * 24));

    return Math.max(0, diffDays);
  }

  /**
   * Format price from cents to formatted currency string.
   */
  formatPrice(priceCents: number, currency: string): string {
    const amount = (priceCents / 100).toFixed(2);
    return `${currency} ${amount}`;
  }

  /**
   * Check if a plan is an upgrade from the current plan (by price).
   */
  isUpgrade(currentPlan: SubscriptionPlan | null | undefined, newPlan: SubscriptionPlan): boolean {
    if (!currentPlan) {
      return true;
    }
    return newPlan.priceCents > currentPlan.priceCents;
  }

  /**
   * Check if a plan is a downgrade from the current plan (by price).
   */
  isDowngrade(currentPlan: SubscriptionPlan | null | undefined, newPlan: SubscriptionPlan): boolean {
    if (!currentPlan) {
      return false;
    }
    return newPlan.priceCents < currentPlan.priceCents;
  }
}
