import { Component } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormBuilder, FormGroup, Validators, ReactiveFormsModule } from '@angular/forms';
import { Router, RouterModule } from '@angular/router';
import { MatIconModule } from '@angular/material/icon';
import { TranslateModule, TranslateService } from '@ngx-translate/core';

import { AuthService } from '../../../core/auth/auth.service';

/**
 * Login component handles user authentication.
 * Uses mock users today and will swap to the ASP.NET Core backend without UI changes later.
 */
@Component({
  selector: 'app-login',
  standalone: true,
  templateUrl: './login.html',
  styleUrls: ['./login.scss'],
  imports: [
    CommonModule,
    ReactiveFormsModule,
    RouterModule,
    MatIconModule,
    TranslateModule
  ],
})
export class LoginComponent {
  hide = true;
  loginForm: FormGroup;
  isSubmitting = false;
  errorMessageKey: string | null = null;

  constructor(
    private fb: FormBuilder,
    private authService: AuthService,
    private translate: TranslateService,
    private router: Router
  ) {
    this.loginForm = this.fb.group({
      email: ['', [Validators.required, Validators.email]],
      password: ['', [Validators.required]]
    });
  }

  onSubmit() {
    if (this.loginForm.invalid) {
      this.loginForm.markAllAsTouched();
      return;
    }

    this.errorMessageKey = null;
    this.isSubmitting = true;
    const { email, password } = this.loginForm.value;

    // Swap this mock call with a backend HTTP call once IdentityServer is wired.
    this.authService.login(email, password).subscribe({
      next: (response) => {
        this.isSubmitting = false;
        const target = response.permissions.includes('ADMIN.DASHBOARD.VIEW')
          ? '/app/admin'
          : '/app/dashboard';
        this.router.navigateByUrl(target);
      },
      error: (err) => {
        this.isSubmitting = false;
        this.errorMessageKey =
          err?.message === 'invalid-credentials'
            ? 'AUTH.ERROR_INVALID_CREDENTIALS'
            : 'AUTH.LOGIN_FAILED';
      },
    });
  }
}
