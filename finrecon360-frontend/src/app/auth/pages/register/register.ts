import { Component } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormBuilder, FormGroup, Validators, ReactiveFormsModule } from '@angular/forms';
import { RouterModule } from '@angular/router';
import { MatIconModule } from '@angular/material/icon';

import { COUNTRIES } from '../../../core/constants/countries';
import { AuthService } from '../../../core/auth/auth.service';

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
export class RegisterComponent {
  registerForm: FormGroup;
  hidePassword = true;
  hideConfirmPassword = true;
  isSubmitting = false;
  isSubmitted = false;
  errorMessage: string | null = null;

  countries = COUNTRIES;

  constructor(private fb: FormBuilder, private authService: AuthService) {
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
