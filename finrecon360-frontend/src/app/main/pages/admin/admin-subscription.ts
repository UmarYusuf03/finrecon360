import { Component, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';

import { AuthService } from '../../../core/auth/auth.service';
import {
  SubscriptionOverview,
  SubscriptionPlan,
} from '../../../core/admin-tenant/models';
import { SubscriptionService } from '../../../core/admin-tenant/subscription.service';

@Component({
  selector: 'app-admin-subscription',
  standalone: true,
  imports: [CommonModule, FormsModule],
  templateUrl: './admin-subscription.html',
  styleUrls: ['./admin-subscription.scss'],
})
export class AdminSubscriptionComponent implements OnInit {
  overview: SubscriptionOverview | null = null;
  plans: SubscriptionPlan[] = [];
  selectedPlanId = '';
  loading = true;
  busy = false;
  error: string | null = null;

  constructor(private authService: AuthService, private subscriptionService: SubscriptionService) {}

  ngOnInit(): void {
    this.subscriptionService.getTenantSubscriptionOverview().subscribe({
      next: (overview) => {
        this.overview = overview;
        this.plans = overview.availablePlans;
        this.selectedPlanId =
          (overview.currentSubscription
            ? this.plans.find((plan) => plan.code === overview.currentSubscription?.planCode)?.id
            : null) ?? this.plans[0]?.id ?? '';
        this.loading = false;
      },
      error: () => {
        this.error = 'Unable to load your subscription details.';
        this.loading = false;
      },
    });
  }

  changePlan(): void {
    if (!this.selectedPlanId) {
      return;
    }

    this.busy = true;
    this.error = null;

    this.subscriptionService.createTenantCheckout(this.selectedPlanId).subscribe({
      next: (response) => {
        window.location.href = response.checkoutUrl;
      },
      error: (error) => {
        const message = error?.error?.message as string | undefined;
        this.error = message ?? 'Unable to start checkout.';
        this.busy = false;
      },
    });
  }

  get currentTenantName(): string {
    return this.authService.currentUser?.tenantName ?? 'Your tenant';
  }
}