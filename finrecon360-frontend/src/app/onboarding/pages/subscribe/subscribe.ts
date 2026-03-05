import { Component, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
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
  maxAccounts: number;
}

@Component({
  selector: 'app-onboarding-subscribe',
  standalone: true,
  imports: [CommonModule],
  templateUrl: './subscribe.html',
  styleUrls: ['./subscribe.scss'],
})
export class OnboardingSubscribeComponent implements OnInit {
  plans: PublicPlan[] = [];
  loading = true;
  error: string | null = null;
  onboardingToken: string | null = null;
  tenantName: string | null = null;

  constructor(private http: HttpClient, private authService: AuthService, private router: Router) {}

  ngOnInit(): void {
    this.onboardingToken = sessionStorage.getItem('fr360_onboarding_token');
    this.tenantName = sessionStorage.getItem('fr360_onboarding_tenant');
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

    this.loading = true;
    this.authService
      .createOnboardingCheckout({ onboardingToken: this.onboardingToken, planId: plan.id })
      .subscribe({
        next: (response) => {
          window.location.href = response.checkoutUrl;
        },
        error: () => {
          this.error = 'Unable to start checkout. Please try again.';
          this.loading = false;
        },
      });
  }
}
