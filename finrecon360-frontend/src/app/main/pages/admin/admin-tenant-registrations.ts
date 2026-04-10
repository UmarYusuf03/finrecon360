import { Component, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { HttpErrorResponse } from '@angular/common/http';
import { FormsModule } from '@angular/forms';
import { Observable } from 'rxjs';
import { map } from 'rxjs/operators';

import { TenantRegistrationService } from '../../../core/admin-tenant/tenant-registration.service';
import { TenantRegistrationApprovalResult, TenantRegistrationSummary } from '../../../core/admin-tenant/models';

@Component({
  selector: 'app-admin-tenant-registrations',
  standalone: true,
  imports: [CommonModule, FormsModule],
  templateUrl: './admin-tenant-registrations.html',
  styleUrls: ['./admin-tenant-registrations.scss'],
})
export class AdminTenantRegistrationsComponent implements OnInit {
  registrations: TenantRegistrationSummary[] = [];
  loading = true;
  processing = false;
  statusFilter = 'PENDING_REVIEW';
  actionMessage: { type: 'success' | 'error'; text: string } | null = null;
  onboardingFallbackLink: string | null = null;

  confirmDialogOpen = false;
  selectedRegistration: TenantRegistrationSummary | null = null;
  confirmAction: 'approve' | 'reject' = 'approve';
  reviewNote = '';

  constructor(private service: TenantRegistrationService) {}

  ngOnInit(): void {
    this.load();
  }

  load(): void {
    this.loading = true;
    this.service.getRegistrations(this.statusFilter).subscribe({
      next: (items) => {
        this.registrations = items;
        this.loading = false;
      },
      error: (error: unknown) => {
        this.loading = false;
        this.actionMessage = {
          type: 'error',
          text: this.extractErrorMessage(error),
        };
      },
    });
  }

  openApproveDialog(reg: TenantRegistrationSummary): void {
    this.openDialog(reg, 'approve');
  }

  openRejectDialog(reg: TenantRegistrationSummary): void {
    this.openDialog(reg, 'reject');
  }

  closeDialog(): void {
    this.confirmDialogOpen = false;
    this.selectedRegistration = null;
    this.reviewNote = '';
  }

  submitReview(): void {
    if (!this.selectedRegistration || this.processing) {
      return;
    }

    this.processing = true;
    const note = this.reviewNote.trim() || undefined;
    const request$: Observable<TenantRegistrationApprovalResult | null> = this.confirmAction === 'approve'
      ? this.service.approve(this.selectedRegistration.id, note)
      : this.service.reject(this.selectedRegistration.id, note).pipe(map(() => null));

    request$.subscribe({
      next: (response: TenantRegistrationApprovalResult | null) => {
        const actionText = this.confirmAction === 'approve' ? 'approved' : 'rejected';
        this.processing = false;
        this.closeDialog();

        this.onboardingFallbackLink = null;
        let text = `Registration ${actionText} successfully.`;

        if (this.confirmAction === 'approve' && response) {
          if (response.emailSent) {
            text = `Registration approved and onboarding email sent to ${response.adminEmail}.`;
          } else if (response.onboardingLink) {
            text = `Registration approved, but email could not be sent. Share the onboarding link below.`;
            this.onboardingFallbackLink = response.onboardingLink;
          } else {
            text = `Registration approved, but onboarding email failed: ${response.emailError ?? 'unknown error'}.`;
          }
        }

        this.actionMessage = {
          type: 'success',
          text,
        };
        this.load();
      },
      error: (error: unknown) => {
        this.processing = false;
        this.actionMessage = {
          type: 'error',
          text: this.extractErrorMessage(error),
        };
      },
    });
  }

  dismissMessage(): void {
    this.actionMessage = null;
    this.onboardingFallbackLink = null;
  }

  private openDialog(reg: TenantRegistrationSummary, action: 'approve' | 'reject'): void {
    this.selectedRegistration = reg;
    this.confirmAction = action;
    this.reviewNote = '';
    this.confirmDialogOpen = true;
    this.actionMessage = null;
    this.onboardingFallbackLink = null;
  }

  private extractErrorMessage(error: unknown): string {
    if (error instanceof HttpErrorResponse) {
      const body = error.error as { message?: string } | null;
      return body?.message ?? `Request failed with status ${error.status}.`;
    }

    return 'Request failed.';
  }
}
