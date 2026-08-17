import { AfterViewInit, Component, ElementRef, OnDestroy, ViewChild } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormBuilder, FormGroup, Validators, ReactiveFormsModule } from '@angular/forms';
import { Router, RouterModule } from '@angular/router';
import { MatIconModule } from '@angular/material/icon';
import { TranslateModule, TranslateService } from '@ngx-translate/core';
import { Subscription } from 'rxjs';

import { AuthService } from '../../../core/auth/auth.service';
import { CurrentUser } from '../../../core/auth/models';
import { GoogleSsoService } from '../../../core/auth/google-sso.service';

/**
 * Login component handles user authentication.
 * Uses mock users today and will swap to the ASP.NET Core backend without UI changes later.
 */
@Component({
  selector: 'app-login',
  standalone: true,
  templateUrl: './login.html',
  styleUrls: ['./login.scss'],
  imports: [CommonModule, ReactiveFormsModule, RouterModule, MatIconModule, TranslateModule],
})
export class LoginComponent implements AfterViewInit, OnDestroy {
  hide = true;
  loginForm: FormGroup;
  isSubmitting = false;
  errorMessageKey: string | null = null;
  errorMessage: string | null = null;

  /** Google's own button is rendered into this element when SSO is configured. */
  @ViewChild('googleButton') googleButtonRef?: ElementRef<HTMLElement>;

  googleSsoAvailable = false;
  isGoogleSubmitting = false;

  private readonly subscriptions = new Subscription();

  constructor(
    private fb: FormBuilder,
    private authService: AuthService,
    private googleSso: GoogleSsoService,
    private translate: TranslateService,
    private router: Router,
  ) {
    this.loginForm = this.fb.group({
      email: ['', [Validators.required, Validators.email]],
      password: ['', [Validators.required]],
    });
  }

  ngAfterViewInit(): void {
    this.setUpGoogleSignIn();
  }

  ngOnDestroy(): void {
    this.subscriptions.unsubscribe();
  }

  /**
   * Asks the backend whether Google sign-in is configured, and only then renders the button.
   * A button that cannot possibly work is worse than no button — it reads as a broken feature.
   */
  private setUpGoogleSignIn(): void {
    const container = this.googleButtonRef?.nativeElement;
    if (!container) {
      return;
    }

    this.subscriptions.add(
      this.googleSso.renderSignInButton(container).subscribe({
        next: (idToken) => {
          this.googleSsoAvailable = true;
          this.signInWithGoogle(idToken);
        },
        error: () => {
          // Not configured, offline, or the script was blocked. Leave the password form as the
          // only route rather than surfacing an error the user cannot act on.
          this.googleSsoAvailable = false;
        },
      }),
    );

    this.subscriptions.add(
      this.googleSso.getConfig().subscribe((config) => {
        this.googleSsoAvailable = config.googleEnabled;
      }),
    );
  }

  private signInWithGoogle(idToken: string): void {
    this.errorMessageKey = null;
    this.errorMessage = null;
    this.isGoogleSubmitting = true;

    this.subscriptions.add(
      this.authService.loginWithGoogle(idToken).subscribe({
        next: (response) => {
          this.isGoogleSubmitting = false;
          this.router.navigateByUrl(this.resolveLandingRoute(response));
        },
        error: (err) => {
          this.isGoogleSubmitting = false;
          this.errorMessage = err?.error?.message ?? null;
          this.errorMessageKey = this.errorMessage ? null : 'AUTH.LOGIN_FAILED';
        },
      }),
    );
  }

  /**
   * Where a signed-in user lands. Identical for password and Google sign-in — the route depends
   * on what the account may do, never on how the person proved who they are.
   *
   * WHY profile is the fallback: /app/dashboard is itself guarded by ADMIN.DASHBOARD.VIEW, so
   * sending a user who lacks that permission there guaranteed an immediate bounce to
   * "Not authorized" — a successful sign-in that looks like a failure. Profile is the one
   * tenant route with no permission requirement, so it is somewhere a brand-new account can
   * genuinely go. A fresh SSO account has an identity but no roles yet, and this is the first
   * sign-in route that produces one.
   */
  private resolveLandingRoute(user: CurrentUser): string {
    if (!user.permissions.includes('ADMIN.DASHBOARD.VIEW')) {
      return '/app/profile';
    }

    // A tenant admin lands on the dashboard, not the administration area. Administration is
    // configuration work done occasionally; the dashboard is the reason someone opens the
    // product on a given morning. System admins have no tenant dashboard, so they keep the
    // control-plane landing.
    if (!user.isSystemAdmin) {
      return '/app/dashboard';
    }

    return user.isSystemAdmin ? '/app/system' : '/app/admin';
  }

  onSubmit() {
    if (this.loginForm.invalid) {
      this.loginForm.markAllAsTouched();
      return;
    }

    this.errorMessageKey = null;
    this.errorMessage = null;
    this.isSubmitting = true;
    const { email, password } = this.loginForm.value;

    this.authService.login(email, password).subscribe({
      next: (response) => {
        this.isSubmitting = false;
        this.router.navigateByUrl(this.resolveLandingRoute(response));
      },
      error: (err) => {
        this.isSubmitting = false;
        this.errorMessage = err?.error?.message ?? err?.message ?? null;
        this.errorMessageKey =
          err?.message === 'invalid-credentials'
            ? 'AUTH.ERROR_INVALID_CREDENTIALS'
            : 'AUTH.LOGIN_FAILED';
      },
    });
  }
}
