import { Component, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { MatIconModule } from '@angular/material/icon';

import { AuthService } from '../../../core/auth/auth.service';
import { API_BASE_URL } from '../../../core/constants/api.constants';
import {
  SubscriptionOverview,
  SubscriptionPlanOption,
} from '../../../core/admin-tenant/models';
import { SubscriptionService } from '../../../core/admin-tenant/subscription.service';

@Component({
  selector: 'app-admin-subscription',
  standalone: true,
  imports: [CommonModule, FormsModule, MatIconModule],
  templateUrl: './admin-subscription.html',
  styleUrls: ['./admin-subscription.scss'],
})
export class AdminSubscriptionComponent implements OnInit {
  overview: SubscriptionOverview | null = null;
  plans: SubscriptionPlanOption[] = [];
  selectedPlanId = '';
  currentPlanId: string | null = null;
  loading = true;
  busy = false;
  error: string | null = null;

  constructor(private authService: AuthService, private subscriptionService: SubscriptionService) {}

  get hasEligiblePlan(): boolean {
    return this.plans.some((plan) => plan.isEligible);
  }

  ngOnInit(): void {
    this.subscriptionService.getTenantSubscriptionOverview().subscribe({
      next: (overview) => {
        this.overview = overview;
        this.plans = overview.availablePlans;

        const currentPlan = overview.currentSubscription
          ? this.plans.find((plan) => plan.code === overview.currentSubscription?.planCode)
          : undefined;
        this.currentPlanId = currentPlan?.id ?? null;
        
        const firstEligiblePlan = this.plans.find((plan) => plan.isEligible);

        // Never land the selection on a plan the tenant can no longer fit into — pick their
        // current plan only if it's still eligible, otherwise the cheapest one that is.
        this.selectedPlanId = (currentPlan?.isEligible ? currentPlan.id : firstEligiblePlan?.id) ?? '';
        this.loading = false;
      },
      error: (error) => {
        const message = error?.error?.detail ?? error?.error?.message;
        this.error = message ? `Unable to load your subscription details: ${message}` : 'Unable to load your subscription details.';
        this.loading = false;
      },
    });
  }

  selectPlan(plan: SubscriptionPlanOption): void {
    if (!plan.isEligible || this.busy) {
      return;
    }

    this.selectedPlanId = plan.id;
  }

  changePlan(): void {
    if (!this.selectedPlanId) {
      return;
    }

    this.busy = true;
    this.error = null;

    this.subscriptionService.createTenantCheckout(this.selectedPlanId).subscribe({
      next: (response) => {
        window.location.href = this.resolveCheckoutUrl(response.checkoutUrl);
      },
      error: (error) => {
        const message = error?.error?.detail ?? error?.error?.message;
        this.error = message ?? 'Unable to start checkout.';
        this.busy = false;
      },
    });
  }

  get currentTenantName(): string {
    return this.authService.currentUser?.tenantName ?? 'Your tenant';
  }

  private resolveCheckoutUrl(checkoutUrl: string): string {
    if (/^https?:\/\//i.test(checkoutUrl)) {
      return checkoutUrl;
    }

    return `${API_BASE_URL}${checkoutUrl.startsWith('/') ? '' : '/'}${checkoutUrl}`;
  }
}