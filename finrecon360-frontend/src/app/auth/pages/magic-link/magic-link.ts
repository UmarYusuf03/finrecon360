import { Component, OnDestroy, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { HttpErrorResponse } from '@angular/common/http';
import { ActivatedRoute, Router, RouterModule } from '@angular/router';
import { FormBuilder, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { Subject, takeUntil } from 'rxjs';

import { AuthService } from '../../../core/auth/auth.service';

type MagicLinkPurpose = 'EmailVerify' | 'PasswordReset' | 'ChangePassword' | 'TenantOnboarding';

type MagicLinkState = 'loading' | 'ready' | 'success' | 'error';

@Component({
  selector: 'app-magic-link',
  standalone: true,
  imports: [CommonModule, ReactiveFormsModule, RouterModule],
  templateUrl: './magic-link.html',
  styleUrls: ['./magic-link.scss'],
})
export class MagicLinkComponent implements OnInit, OnDestroy {
  private static readonly PendingOnboardingTokenKey = 'fr360_pending_onboarding_token';
  private static readonly PendingOnboardingTenantKey = 'fr360_pending_onboarding_tenant';
  private static readonly PendingOnboardingRequestedBankAccountsKey = 'fr360_pending_onboarding_requested_bank_accounts';
  private static readonly PendingMagicLinkTokenKey = 'fr360_pending_magic_link_token';

  state: MagicLinkState = 'loading';
  purpose: MagicLinkPurpose | null = null;
  token: string | null = null;
  errorMessage: string | null = null;

  resetForm!: FormGroup;
  changeForm!: FormGroup;
  onboardingForm!: FormGroup;
  onboardingToken: string | null = null;
  tenantName: string | null = null;
  requestedBankAccounts: number | null = null;

  private destroy$ = new Subject<void>();

  constructor(private route: ActivatedRoute, private fb: FormBuilder, private authService: AuthService, private router: Router) {}

  ngOnInit(): void {
    this.resetForm = this.fb.group(
      {
        newPassword: ['', [Validators.required, Validators.minLength(8)]],
        confirmPassword: ['', Validators.required],
      },
      { validators: this.matchingPasswords('newPassword', 'confirmPassword') }
    );

    this.changeForm = this.fb.group(
      {
        currentPassword: ['', Validators.required],
        newPassword: ['', [Validators.required, Validators.minLength(8)]],
        confirmPassword: ['', Validators.required],
      },
      { validators: this.matchingPasswords('newPassword', 'confirmPassword') }
    );

    this.onboardingForm = this.fb.group(
      {
        password: ['', [Validators.required, Validators.minLength(8)]],
        confirmPassword: ['', Validators.required],
      },
      { validators: this.matchingPasswords('password', 'confirmPassword') }
    );

    this.route.queryParamMap.pipe(takeUntil(this.destroy$)).subscribe((params) => {
      const purpose = params.get('purpose') as MagicLinkPurpose | null;
      const token = params.get('token');
      this.purpose = purpose;
      this.token = token;

      if (!purpose || !token) {
        this.state = 'error';
        this.errorMessage = 'Invalid or missing magic link.';
        return;
      }

      if (purpose === 'EmailVerify') {
        this.verifyEmail(token);
        return;
      }

      if (purpose === 'TenantOnboarding') {
        const pendingOnboardingToken = sessionStorage.getItem(MagicLinkComponent.PendingOnboardingTokenKey);
        const pendingTenantName = sessionStorage.getItem(MagicLinkComponent.PendingOnboardingTenantKey);
        const pendingRequestedBankAccounts = sessionStorage.getItem(
          MagicLinkComponent.PendingOnboardingRequestedBankAccountsKey
        );
        const pendingMagicLinkToken = sessionStorage.getItem(MagicLinkComponent.PendingMagicLinkTokenKey);

        if (pendingOnboardingToken && pendingMagicLinkToken === token) {
          this.onboardingToken = pendingOnboardingToken;
          this.tenantName = pendingTenantName;
          this.requestedBankAccounts = this.parseRequestedBankAccounts(pendingRequestedBankAccounts);
          this.state = 'ready';
          return;
        }

        this.verifyOnboarding(token);
        return;
      }

      if (purpose === 'PasswordReset' || purpose === 'ChangePassword') {
        this.state = 'ready';
        return;
      }

      this.state = 'error';
      this.errorMessage = 'Unsupported magic link purpose.';
    });
  }

  ngOnDestroy(): void {
    this.destroy$.next();
    this.destroy$.complete();
  }

  submitPasswordReset(): void {
    if (!this.token || this.resetForm.invalid) {
      this.resetForm.markAllAsTouched();
      return;
    }

    const newPassword = this.resetForm.get('newPassword')?.value;
    this.state = 'loading';
    this.errorMessage = null;

    this.authService.confirmPasswordResetLink(this.token, newPassword).subscribe({
      next: () => {
        this.state = 'success';
        this.token = null;
      },
      error: () => {
        this.state = 'error';
        this.errorMessage = 'This reset link is invalid or expired.';
      },
    });
  }

  submitChangePassword(): void {
    if (!this.token || this.changeForm.invalid) {
      this.changeForm.markAllAsTouched();
      return;
    }

    const currentPassword = this.changeForm.get('currentPassword')?.value;
    const newPassword = this.changeForm.get('newPassword')?.value;

    this.state = 'loading';
    this.errorMessage = null;

    this.authService.confirmChangePasswordLink(this.token, currentPassword, newPassword).subscribe({
      next: () => {
        this.state = 'success';
        this.token = null;
      },
      error: () => {
        this.state = 'error';
        this.errorMessage = 'This change password link is invalid or expired.';
      },
    });
  }

  submitOnboardingPassword(): void {
    if (!this.onboardingToken || !this.token || this.onboardingForm.invalid) {
      this.onboardingForm.markAllAsTouched();
      return;
    }

    const password = this.onboardingForm.get('password')?.value;
    const confirmPassword = this.onboardingForm.get('confirmPassword')?.value;
    this.state = 'loading';
    this.errorMessage = null;

    this.authService
      .setOnboardingPassword({ onboardingToken: this.onboardingToken, magicLinkToken: this.token, password, confirmPassword })
      .subscribe({
        next: () => {
          sessionStorage.removeItem(MagicLinkComponent.PendingOnboardingTokenKey);
          sessionStorage.removeItem(MagicLinkComponent.PendingOnboardingTenantKey);
          sessionStorage.removeItem(MagicLinkComponent.PendingOnboardingRequestedBankAccountsKey);
          sessionStorage.removeItem(MagicLinkComponent.PendingMagicLinkTokenKey);

          sessionStorage.setItem('fr360_onboarding_token', this.onboardingToken!);
          sessionStorage.setItem('fr360_onboarding_tenant', this.tenantName ?? '');
          if (this.requestedBankAccounts && this.requestedBankAccounts > 0) {
            sessionStorage.setItem('fr360_onboarding_requested_bank_accounts', this.requestedBankAccounts.toString());
          } else {
            sessionStorage.removeItem('fr360_onboarding_requested_bank_accounts');
          }
          this.router.navigateByUrl('/onboarding/subscribe');
        },
        error: (error: unknown) => {
          this.state = 'error';
          if (error instanceof HttpErrorResponse) {
            const body = error.error as { message?: string } | null;
            this.errorMessage = body?.message ?? 'Unable to set password. Please request a new link.';
          } else {
            this.errorMessage = 'Unable to set password. Please request a new link.';
          }
        },
      });
  }

  private verifyEmail(token: string): void {
    this.state = 'loading';
    this.errorMessage = null;

    this.authService.verifyEmailLink(token).subscribe({
      next: () => {
        this.state = 'success';
        this.token = null;
      },
      error: () => {
        this.state = 'error';
        this.errorMessage = 'This verification link is invalid or expired.';
      },
    });
  }

  private verifyOnboarding(token: string): void {
    this.state = 'loading';
    this.errorMessage = null;

    this.authService.verifyOnboardingMagicLink(token).subscribe({
      next: (result) => {
        this.onboardingToken = result.onboardingToken;
        this.tenantName = result.tenantName;
        this.requestedBankAccounts = result.requestedBankAccounts;

        sessionStorage.setItem(MagicLinkComponent.PendingOnboardingTokenKey, result.onboardingToken);
        sessionStorage.setItem(MagicLinkComponent.PendingOnboardingTenantKey, result.tenantName ?? '');
        if (result.requestedBankAccounts && result.requestedBankAccounts > 0) {
          sessionStorage.setItem(
            MagicLinkComponent.PendingOnboardingRequestedBankAccountsKey,
            result.requestedBankAccounts.toString()
          );
        } else {
          sessionStorage.removeItem(MagicLinkComponent.PendingOnboardingRequestedBankAccountsKey);
        }
        sessionStorage.setItem(MagicLinkComponent.PendingMagicLinkTokenKey, token);

        this.state = 'ready';
      },
      error: (error: unknown) => {
        const pendingOnboardingToken = sessionStorage.getItem(MagicLinkComponent.PendingOnboardingTokenKey);
        const pendingTenantName = sessionStorage.getItem(MagicLinkComponent.PendingOnboardingTenantKey);
        const pendingRequestedBankAccounts = sessionStorage.getItem(
          MagicLinkComponent.PendingOnboardingRequestedBankAccountsKey
        );
        const pendingMagicLinkToken = sessionStorage.getItem(MagicLinkComponent.PendingMagicLinkTokenKey);

        if (pendingOnboardingToken && pendingMagicLinkToken === token) {
          this.onboardingToken = pendingOnboardingToken;
          this.tenantName = pendingTenantName;
          this.requestedBankAccounts = this.parseRequestedBankAccounts(pendingRequestedBankAccounts);
          this.state = 'ready';
          return;
        }

        this.state = 'error';
        if (error instanceof HttpErrorResponse) {
          const body = error.error as { message?: string } | null;
          this.errorMessage = body?.message ?? 'This onboarding link is invalid or expired.';
        } else {
          this.errorMessage = 'This onboarding link is invalid or expired.';
        }
      },
    });
  }

  private matchingPasswords(passwordKey: string, confirmKey: string) {
    return (group: FormGroup) => {
      const password = group.get(passwordKey)?.value;
      const confirm = group.get(confirmKey)?.value;
      if (password && confirm && password !== confirm) {
        group.get(confirmKey)?.setErrors({ mismatch: true });
      } else {
        const errors = group.get(confirmKey)?.errors;
        if (errors) {
          delete errors['mismatch'];
          if (!Object.keys(errors).length) {
            group.get(confirmKey)?.setErrors(null);
          }
        }
      }
      return null;
    };
  }

  private parseRequestedBankAccounts(value: string | null): number | null {
    if (!value) {
      return null;
    }

    const parsed = Number(value);
    return Number.isInteger(parsed) && parsed > 0 ? parsed : null;
  }
}
