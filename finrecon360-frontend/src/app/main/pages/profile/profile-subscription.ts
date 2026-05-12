import { Component, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { HttpErrorResponse } from '@angular/common/http';
import { MatCardModule } from '@angular/material/card';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { MatTooltipModule } from '@angular/material/tooltip';

import {
  SubscriptionOverview,
  SubscriptionPlan,
  TenantSubscription,
} from '../../../core/admin-tenant/models';
import { ProfileSubscriptionService } from '../../../core/services/profile-subscription.service';

interface PlanWithComparison extends SubscriptionPlan {
  isCurrent: boolean;
  isUpgrade: boolean;
  isDowngrade: boolean;
}

@Component({
  selector: 'app-profile-subscription',
  standalone: true,
  imports: [
    CommonModule,
    MatCardModule,
    MatButtonModule,
    MatIconModule,
    MatProgressBarModule,
    MatTooltipModule,
  ],
  templateUrl: './profile-subscription.html',
  styleUrls: ['./profile-subscription.scss'],
})
export class ProfileSubscriptionComponent implements OnInit {
  overview: SubscriptionOverview | null = null;
  plans: PlanWithComparison[] = [];
  selectedPlanId: string | null = null;
  loading = true;
  changing = false;
  error: string | null = null;
  successMessage: string | null = null;

  // Days remaining calculation
  daysRemaining = 0;
  totalDays = 30;
  progressPercentage = 100;

  constructor(private profileSubscriptionService: ProfileSubscriptionService) {}

  ngOnInit(): void {
    this.loadSubscription();
  }

  private loadSubscription(): void {
    this.loading = true;
    this.error = null;
    this.successMessage = null;

    this.profileSubscriptionService.getProfileSubscriptionOverview().subscribe({
      next: (overview) => {
        this.overview = overview;
        this.calculateDaysRemaining();
        this.buildPlanComparisons();
        this.loading = false;
      },
      error: (err: unknown) => {
        this.loading = false;
        if (err instanceof HttpErrorResponse) {
          if (err.status === 403) {
            this.error = 'You do not have permission to view subscription details.';
          } else {
            this.error = err.error?.message ?? 'Unable to load subscription details.';
          }
        } else {
          this.error = 'Unable to load subscription details.';
        }
      },
    });
  }

  private calculateDaysRemaining(): void {
    if (!this.overview?.currentSubscription?.periodEnd) {
      this.daysRemaining = 0;
      this.progressPercentage = 0;
      return;
    }

    const periodEnd = new Date(this.overview.currentSubscription.periodEnd);
    const periodStart = this.overview.currentSubscription.periodStart
      ? new Date(this.overview.currentSubscription.periodStart)
      : new Date(periodEnd.getTime() - 30 * 24 * 60 * 60 * 1000);

    const now = new Date();
    const totalDuration = periodEnd.getTime() - periodStart.getTime();
    const elapsed = now.getTime() - periodStart.getTime();

    this.daysRemaining = this.profileSubscriptionService.calculateDaysRemaining(
      this.overview.currentSubscription.periodEnd
    );
    this.totalDays = Math.ceil(totalDuration / (1000 * 60 * 60 * 24));
    this.progressPercentage = Math.max(0, Math.min(100, (elapsed / totalDuration) * 100));
  }

  private buildPlanComparisons(): void {
    if (!this.overview) {
      this.plans = [];
      return;
    }

    const currentPlanCode = this.overview.currentSubscription?.planCode;

    this.plans = this.overview.availablePlans.map((plan) => {
      const isCurrent = plan.code === currentPlanCode;
      const isUpgrade = this.profileSubscriptionService.isUpgrade(
        this.getCurrentPlanFromAvailable(),
        plan
      );
      const isDowngrade = this.profileSubscriptionService.isDowngrade(
        this.getCurrentPlanFromAvailable(),
        plan
      );

      return {
        ...plan,
        isCurrent,
        isUpgrade,
        isDowngrade,
      };
    });

    // Pre-select current plan if exists
    const currentPlan = this.plans.find((p) => p.isCurrent);
    if (currentPlan) {
      this.selectedPlanId = currentPlan.id;
    } else if (this.plans.length > 0) {
      this.selectedPlanId = this.plans[0].id;
    }
  }

  private getCurrentPlanFromAvailable(): SubscriptionPlan | undefined {
    if (!this.overview?.currentSubscription) {
      return undefined;
    }
    return this.overview.availablePlans.find(
      (p) => p.code === this.overview?.currentSubscription?.planCode
    );
  }

  selectPlan(planId: string): void {
    this.selectedPlanId = planId;
    this.error = null;
    this.successMessage = null;
  }

  changePlan(): void {
    if (!this.selectedPlanId) {
      this.error = 'Please select a plan first.';
      return;
    }

    const selectedPlan = this.plans.find((p) => p.id === this.selectedPlanId);
    if (!selectedPlan) {
      this.error = 'Selected plan not found.';
      return;
    }

    // If same plan, no need to change
    if (selectedPlan.isCurrent) {
      this.successMessage = 'You are already on this plan.';
      return;
    }

    // Confirm before changing
    const action = selectedPlan.isDowngrade ? 'downgrade' : 'upgrade';
    const message = selectedPlan.isDowngrade
      ? `Are you sure you want to downgrade to ${selectedPlan.name}? Your current features will be reduced.`
      : `Upgrade to ${selectedPlan.name} for ${this.formatPrice(selectedPlan.priceCents, selectedPlan.currency)}?`;

    if (!confirm(message)) {
      return;
    }

    this.changing = true;
    this.error = null;
    this.successMessage = null;

    this.profileSubscriptionService.createProfileCheckout(this.selectedPlanId).subscribe({
      next: (response) => {
        if (response.checkoutUrl) {
          // Redirect to PayHere checkout
          window.location.href = response.checkoutUrl;
        } else {
          this.changing = false;
          this.error = 'Payment gateway did not return a valid checkout URL.';
        }
      },
      error: (err: unknown) => {
        this.changing = false;
        console.error('[Profile Subscription] Checkout error:', err);

        if (err instanceof HttpErrorResponse) {
          if (err.status === 403) {
            this.error = 'You do not have permission to change subscription plans.';
          } else {
            this.error = err.error?.message ?? 'Unable to start checkout. Please try again.';
          }
        } else {
          this.error = 'Unable to start checkout. Please try again.';
        }
      },
    });
  }

  formatPrice(priceCents: number, currency: string): string {
    return this.profileSubscriptionService.formatPrice(priceCents, currency);
  }

  get currentSubscription(): TenantSubscription | null {
    return this.overview?.currentSubscription ?? null;
  }

  get hasActiveSubscription(): boolean {
    return this.currentSubscription?.status === 'Active';
  }

  get subscriptionStatusClass(): string {
    const status = this.currentSubscription?.status?.toLowerCase() ?? '';
    switch (status) {
      case 'active':
        return 'status-active';
      case 'pending':
      case 'pendingpayment':
        return 'status-pending';
      case 'expired':
      case 'cancelled':
        return 'status-expired';
      default:
        return 'status-default';
    }
  }

  refresh(): void {
    this.loadSubscription();
  }
}
