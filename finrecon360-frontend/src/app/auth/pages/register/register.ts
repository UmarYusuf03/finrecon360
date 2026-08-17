import { AfterViewInit, Component, ElementRef, OnDestroy, ViewChild } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormBuilder, FormGroup, Validators, ReactiveFormsModule } from '@angular/forms';
import { Router, RouterModule } from '@angular/router';
import { MatIconModule } from '@angular/material/icon';
import { Subscription } from 'rxjs';

import { COUNTRIES } from '../../../core/constants/countries';
import { AuthService } from '../../../core/auth/auth.service';
import { CurrentUser } from '../../../core/auth/models';
import { GoogleSsoService } from '../../../core/auth/google-sso.service';

@Component({
  selector: 'app-register',
  standalone: true,
  templateUrl: './register.html',
  styleUrls: ['./register.scss'],
  imports: [
    CommonModule,
    ReactiveFormsModule,
    RouterModule,
    MatIconModule
  ]
})
export class RegisterComponent implements AfterViewInit, OnDestroy {
  registerForm: FormGroup;
  hidePassword = true;
  hideConfirmPassword = true;
  isSubmitting = false;
  isSubmitted = false;
  errorMessage: string | null = null;

  countries = COUNTRIES;

  /** Google renders its own button into this element when SSO is configured. */
  @ViewChild('googleButton') googleButtonRef?: ElementRef<HTMLElement>;

  googleSsoAvailable = false;
  isGoogleSubmitting = false;
  ssoError: string | null = null;

  private readonly subscriptions = new Subscription();

  constructor(
    private fb: FormBuilder,
    private authService: AuthService,
    private googleSso: GoogleSsoService,
    private router: Router,
  ) {
    this.registerForm = this.fb.group({
      fullName: ['', Validators.required],
      dob: ['', Validators.required],
      country: ['', Validators.required],
      email: ['', [Validators.required, Validators.email]],
      gender: ['male', Validators.required],
      password: ['', [Validators.required, Validators.minLength(8)]],
      confirmPassword: ['', Validators.required]
    });
  }

  ngAfterViewInit(): void {
    const container = this.googleButtonRef?.nativeElement;
    if (!container) {
      return;
    }

    this.subscriptions.add(
      this.googleSso.renderSignInButton(container).subscribe({
        next: (idToken) => {
          this.googleSsoAvailable = true;
          this.signUpWithGoogle(idToken);
        },
        // Not configured, offline, or the script was blocked. Leave the password form as the
        // only route rather than surfacing an error the user cannot act on.
        error: () => (this.googleSsoAvailable = false),
      }),
    );

    this.subscriptions.add(
      this.googleSso.getConfig().subscribe((config) => {
        this.googleSsoAvailable = config.googleEnabled;
      }),
    );
  }

  ngOnDestroy(): void {
    this.subscriptions.unsubscribe();
  }

  /**
   * Signing up and signing in with Google are the same server operation: the account is created
   * on first arrival if it does not exist. So this posts the same token to the same endpoint
   * rather than duplicating a registration path that would have to stay in step with it.
   */
  private signUpWithGoogle(idToken: string): void {
    this.ssoError = null;
    this.isGoogleSubmitting = true;

    this.subscriptions.add(
      this.authService.loginWithGoogle(idToken).subscribe({
        next: (user) => {
          this.isGoogleSubmitting = false;
          this.router.navigateByUrl(this.resolveLandingRoute(user));
        },
        error: (err) => {
          this.isGoogleSubmitting = false;
          this.ssoError = err?.error?.message ?? 'Google sign-up failed. Please try again.';
        },
      }),
    );
  }

  /** Matches the login screen: the destination depends on what the account may do. */
  private resolveLandingRoute(user: CurrentUser): string {
    if (!user.permissions.includes('ADMIN.DASHBOARD.VIEW')) {
      return '/app/profile';
    }

    return user.isSystemAdmin ? '/app/system' : '/app/admin';
  }

  onSubmit() {
    if (this.registerForm.invalid) {
      this.registerForm.markAllAsTouched();
      return;
    }

    const { fullName, country, email, gender, password, confirmPassword } = this.registerForm.value;
    const [firstName, ...rest] = String(fullName).trim().split(/\s+/);
    const lastName = rest.length ? rest.join(' ') : 'User';

    this.isSubmitting = true;
    this.errorMessage = null;

    this.authService
      .register({
        email,
        firstName,
        lastName,
        country,
        gender,
        password,
        confirmPassword,
      })
      .subscribe({
        next: () => {
          this.isSubmitting = false;
          this.isSubmitted = true;
        },
        error: () => {
          this.isSubmitting = false;
          this.errorMessage = 'Registration failed. Please try again.';
        },
      });
  }
}
