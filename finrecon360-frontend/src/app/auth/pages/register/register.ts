import { AfterViewInit, Component, ElementRef, OnDestroy, ViewChild } from '@angular/core';
import { CommonModule } from '@angular/common';
import {
  AbstractControl,
  FormBuilder,
  FormGroup,
  ValidationErrors,
  ValidatorFn,
  Validators,
  ReactiveFormsModule,
} from '@angular/forms';
import { Router, RouterModule } from '@angular/router';
import { MatIconModule } from '@angular/material/icon';
import { Subscription } from 'rxjs';

import { COUNTRIES } from '../../../core/constants/countries';
import { AuthService } from '../../../core/auth/auth.service';
import { CurrentUser } from '../../../core/auth/models';
import { GoogleSsoService } from '../../../core/auth/google-sso.service';

/** Cross-field validator: confirmPassword must equal password. */
const passwordMatchValidator: ValidatorFn = (group: AbstractControl): ValidationErrors | null => {
  const pw = group.get('password')?.value;
  const cpw = group.get('confirmPassword')?.value;
  if (!pw || !cpw) return null;
  return pw === cpw ? null : { passwordMismatch: true };
};

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
    this.registerForm = this.fb.group(
      {
        fullName: ['', [Validators.required, Validators.minLength(2)]],
        dob: ['', Validators.required],
        country: ['', Validators.required],
        email: ['', [Validators.required, Validators.email]],
        gender: ['male', Validators.required],
        password: ['', [Validators.required, Validators.minLength(8)]],
        confirmPassword: ['', Validators.required],
      },
      { validators: passwordMatchValidator },
    );
  }

  ngAfterViewInit(): void {
    const container = this.googleButtonRef?.nativeElement;
    if (!container) return;

    this.subscriptions.add(
      this.googleSso.renderSignInButton(container).subscribe({
        next: (idToken) => {
          this.googleSsoAvailable = true;
          this.signUpWithGoogle(idToken);
        },
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

  // ── Field error helpers ──────────────────────────────────────────────────────

  get fullNameError(): string | null {
    const ctrl = this.registerForm.get('fullName');
    if (!ctrl?.invalid || !ctrl.touched) return null;
    if (ctrl.hasError('required')) return 'Full name is required.';
    if (ctrl.hasError('minlength')) return 'Enter at least 2 characters.';
    return null;
  }

  get dobError(): string | null {
    const ctrl = this.registerForm.get('dob');
    if (!ctrl?.invalid || !ctrl.touched) return null;
    return 'Date of birth is required.';
  }

  get countryError(): string | null {
    const ctrl = this.registerForm.get('country');
    if (!ctrl?.invalid || !ctrl.touched) return null;
    return 'Please select a country.';
  }

  get emailError(): string | null {
    const ctrl = this.registerForm.get('email');
    if (!ctrl?.invalid || !ctrl.touched) return null;
    if (ctrl.hasError('required')) return 'Email is required.';
    if (ctrl.hasError('email')) return 'Enter a valid email address.';
    return null;
  }

  get passwordError(): string | null {
    const ctrl = this.registerForm.get('password');
    if (!ctrl?.invalid || !ctrl.touched) return null;
    if (ctrl.hasError('required')) return 'Password is required.';
    if (ctrl.hasError('minlength')) return 'Password must be at least 8 characters.';
    return null;
  }

  get confirmPasswordError(): string | null {
    const ctrl = this.registerForm.get('confirmPassword');
    if (!ctrl?.touched) return null;
    if (ctrl.hasError('required')) return 'Please confirm your password.';
    if (this.registerForm.hasError('passwordMismatch')) return 'Passwords do not match.';
    return null;
  }

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

  private resolveLandingRoute(user: CurrentUser): string {
    if (!user.permissions.includes('ADMIN.DASHBOARD.VIEW')) return '/app/profile';
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
      .register({ email, firstName, lastName, country, gender, password, confirmPassword })
      .subscribe({
        next: () => {
          this.isSubmitting = false;
          this.isSubmitted = true;
        },
        error: (err) => {
          this.isSubmitting = false;
          this.errorMessage =
            err?.error?.message ??
            err?.error?.title ??
            'Registration failed. Please try again.';
        },
      });
  }
}
