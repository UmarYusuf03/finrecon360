import { Component, OnDestroy, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { ActivatedRoute, RouterModule } from '@angular/router';
import { FormBuilder, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { Subject, takeUntil } from 'rxjs';

import { AuthService } from '../../../core/auth/auth.service';

type MagicLinkPurpose = 'EmailVerify' | 'PasswordReset' | 'ChangePassword';

type MagicLinkState = 'loading' | 'ready' | 'success' | 'error';

@Component({
  selector: 'app-magic-link',
  standalone: true,
  imports: [CommonModule, ReactiveFormsModule, RouterModule],
  templateUrl: './magic-link.html',
  styleUrls: ['./magic-link.scss'],
})
export class MagicLinkComponent implements OnInit, OnDestroy {
  state: MagicLinkState = 'loading';
  purpose: MagicLinkPurpose | null = null;
  token: string | null = null;
  errorMessage: string | null = null;

  resetForm!: FormGroup;
  changeForm!: FormGroup;

  private destroy$ = new Subject<void>();

  constructor(private route: ActivatedRoute, private fb: FormBuilder, private authService: AuthService) {}

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
}
