import { Component } from '@angular/core';
import { CommonModule } from '@angular/common';
import { HttpErrorResponse } from '@angular/common/http';
import { FormBuilder, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { Router } from '@angular/router';
import { RouterModule } from '@angular/router';
import { MatIconModule } from '@angular/material/icon';

import { AuthService } from '../../../core/auth/auth.service';

@Component({
  selector: 'app-tenant-register',
  standalone: true,
  imports: [CommonModule, ReactiveFormsModule, RouterModule, MatIconModule],
  templateUrl: './tenant-register.html',
  styleUrls: ['./tenant-register.scss'],
})
export class TenantRegisterComponent {
  readonly businessTypes = [
    { value: 'VEHICLE_RENTAL', label: 'Vehicle Rental' },
    { value: 'ACCOMMODATION', label: 'Accommodation' },
  ];

  form: FormGroup;
  submitting = false;
  success = false;
  error: string | null = null;

  constructor(private fb: FormBuilder, private authService: AuthService, private router: Router) {
    this.form = this.fb.group({
      businessName: ['', Validators.required],
      adminEmail: ['', [Validators.required, Validators.email]],
      phoneNumber: ['', [Validators.required, Validators.pattern(/^[0-9+\-\s()]{7,32}$/)]],
      businessRegistrationNumber: ['', Validators.required],
      businessType: ['', Validators.required],
      bankAccounts: [1, [Validators.required, Validators.min(1)]],
      notes: [''],
    });
  }

  submit(): void {
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }

    this.submitting = true;
    this.error = null;

    const payload = {
      businessName: this.form.value.businessName,
      adminEmail: this.form.value.adminEmail,
      phoneNumber: this.form.value.phoneNumber,
      businessRegistrationNumber: this.form.value.businessRegistrationNumber,
      businessType: this.form.value.businessType,
      onboardingMetadata: {
        bankAccounts: this.form.value.bankAccounts,
        notes: this.form.value.notes,
      },
    };

    this.authService.registerTenant(payload).subscribe({
      next: () => {
        this.success = true;
        this.submitting = false;
        this.router.navigateByUrl('/public/tenant-pending');
      },
      error: (error: unknown) => {
        if (error instanceof HttpErrorResponse) {
          const body = error.error as { message?: string } | null;
          this.error = body?.message ?? 'Unable to submit registration. Please try again.';
        } else {
          this.error = 'Unable to submit registration. Please try again.';
        }
        this.submitting = false;
      },
    });
  }
}
