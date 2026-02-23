import { Component } from '@angular/core';
import { RouterModule } from '@angular/router';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';

import { AuthService } from '../../../core/auth/auth.service';

@Component({
  selector: 'app-forgot-password',
  standalone: true,
  imports: [
    RouterModule,
    CommonModule,
    FormsModule
  ],
  templateUrl: './forgot-password.html',
  styleUrls: ['./forgot-password.scss'],
})
export class ForgotPasswordComponent {
  email = '';
  isSubmitting = false;
  isSubmitted = false;
  errorMessage: string | null = null;

  constructor(private authService: AuthService) {}

  onSubmit() {
    if (!this.email) {
      return;
    }

    this.isSubmitting = true;
    this.errorMessage = null;

    this.authService.requestPasswordResetLink(this.email).subscribe({
      next: () => {
        this.isSubmitting = false;
        this.isSubmitted = true;
      },
      error: () => {
        this.isSubmitting = false;
        this.errorMessage = 'If the email exists, you will receive a reset link.';
        this.isSubmitted = true;
      },
    });
  }
}
