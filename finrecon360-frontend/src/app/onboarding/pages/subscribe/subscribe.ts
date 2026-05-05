import { Component, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { HttpErrorResponse } from '@angular/common/http';
import { HttpClient } from '@angular/common/http';
import { Router } from '@angular/router';

import { API_BASE_URL, API_ENDPOINTS } from '../../../core/constants/api.constants';
import { AuthService } from '../../../core/auth/auth.service';

interface PublicPlan {
  id: string;
  code: string;
  name: string;
  priceCents: number;
  currency: string;
  durationDays: number;
  maxUsers: number;
  maxAccounts: number;
}

@Component({
  selector: 'app-onboarding-subscribe',
  standalone: true,
  imports: [CommonModule],
  templateUrl: './subscribe.html',
  styleUrls: ['./subscribe.scss'],
})
/**
 * WHY: Displays available subscription plans during onboarding and validates plan eligibility against
 * the requested bank account count from the registration form. User selects a plan and initiates
 * PayHere checkout, which redirects to the payment gateway. Session storage holds the onboarding token
 * to survive the redirect round-trip back to the application.
 */
export class OnboardingSubscribeComponent implements OnInit {
  plans: PublicPlan[] = [];
  loading = true;
  error: string | null = null;
  onboardingToken: string | null = null;
  tenantName: string | null = null;
  requestedBankAccounts: number | null = null;

  constructor(private http: HttpClient, private authService: AuthService, private router: Router) {}

  ngOnInit(): void {
    this.onboardingToken = sessionStorage.getItem('fr360_onboarding_token');
    this.tenantName = sessionStorage.getItem('fr360_onboarding_tenant');
    this.requestedBankAccounts = this.parseRequestedBankAccounts(
      sessionStorage.getItem('fr360_onboarding_requested_bank_accounts')
    );
    if (!this.onboardingToken) {
      this.router.navigateByUrl('/auth/login');
      return;
    }

    this.http.get<PublicPlan[]>(`${API_BASE_URL}${API_ENDPOINTS.PUBLIC.PLANS}`).subscribe({
      next: (plans) => {
        this.plans = plans;
        this.loading = false;
      },
      error: () => {
        this.error = 'Unable to load plans. Please try again.';
        this.loading = false;
      },
    });
  }

  selectPlan(plan: PublicPlan): void {
    if (!this.onboardingToken) {
      return;
    }

    if (!this.isPlanEligible(plan)) {
      this.error = `This plan supports up to ${plan.maxAccounts} bank accounts, but your registration requested ${this.requestedBankAccounts}. Please select a compatible plan.`;
      return;
    }

    this.loading = true;
    this.authService
      .createOnboardingCheckout({ onboardingToken: this.onboardingToken, planId: plan.id })
      .subscribe({
        next: (response) => {
          window.location.href = response.checkoutUrl;
        },
        error: (error: unknown) => {
          if (error instanceof HttpErrorResponse) {
            const body = error.error as { message?: string } | null;
            this.error = body?.message ?? 'Unable to start checkout. Please try again.';
          } else {
            this.error = 'Unable to start checkout. Please try again.';
          }
          this.loading = false;
        },
      });
  }

  isPlanEligible(plan: PublicPlan): boolean {
    if (!this.requestedBankAccounts) {
      return true;
    }

    return plan.maxAccounts >= this.requestedBankAccounts;
  }

  private parseRequestedBankAccounts(value: string | null): number | null {
    if (!value) {
      return null;
    }

    const parsed = Number(value);
    return Number.isInteger(parsed) && parsed > 0 ? parsed : null;
  }
}
