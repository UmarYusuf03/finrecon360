import { Component, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { HttpErrorResponse } from '@angular/common/http';
import { MatButtonModule } from '@angular/material/button';
import { MatChipsModule } from '@angular/material/chips';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatSnackBar, MatSnackBarModule } from '@angular/material/snack-bar';
import { MatTableModule } from '@angular/material/table';
import { MatTooltipModule } from '@angular/material/tooltip';
import { MatSelectModule } from '@angular/material/select';
import { RouterLink } from '@angular/router';

import { ReconciliationService } from '../../../core/admin-rbac/reconciliation.service';
import { ReconciliationEvent } from '../../../core/admin-rbac/models';
import { AuthService } from '../../../core/auth/auth.service';

/**
 * WHY: Sales Verification Queue surfaces Level-3 reconciliation exception records —
 * ERP entries that could not be automatically matched to a Gateway counterpart.
 * An accountant must manually review and resolve these discrepancies before
 * the record can proceed to Level-4 bank matching.
 *
 * Data source: GET /api/admin/reconciliation/events?stage=Level3&status=Pending
 * (Pending events at Level 3 represent unresolved ERP ↔ Gateway discrepancies.)
 */
@Component({
  selector: 'app-matcher-sales-verification',
  standalone: true,
  imports: [
    CommonModule,
    FormsModule,
    MatTableModule,
    MatButtonModule,
    MatIconModule,
    MatChipsModule,
    MatSnackBarModule,
    MatProgressSpinnerModule,
    MatTooltipModule,
    MatSelectModule,
    RouterLink,
  ],
  templateUrl: './matcher-sales-verification.html',
  styleUrls: ['./matcher-sales-verification.scss'],
})
export class MatcherSalesVerificationComponent implements OnInit {
  readonly displayedColumns = [
    'stage',
    'sourceType',
    'eventType',
    'status',
    'createdAt',
    'detail',
  ];

  allEvents: ReconciliationEvent[] = [];
  filteredEvents: ReconciliationEvent[] = [];

  loading = false;
  sourceTypeFilter = '';
  eventTypeFilter = '';

  // Summary counters
  get exceptionCount(): number { return this.allEvents.filter(e => e.status === 'Pending' && e.eventType === 'MatchNotFound').length; }
  get verifiedCount(): number { return this.allEvents.filter(e => e.status === 'Resolved').length; }
  get totalCount(): number { return this.allEvents.length; }

  constructor(
    private reconciliationService: ReconciliationService,
    private authService: AuthService,
    private snackBar: MatSnackBar,
  ) {}

  /**
   * WHY: The rbac_granular_plan requires every page that reads reconciliation data to
   * gate on ADMIN.RECONCILIATION.VIEW. MANAGE is kept as a legacy alias for existing grants.
   */
  get canViewRecon(): boolean {
    const perms = this.authService.currentUser?.permissions ?? [];
    return perms.includes('ADMIN.RECONCILIATION.VIEW')
      || perms.includes('ADMIN.RECONCILIATION.CONFIRM')
      || perms.includes('ADMIN.RECONCILIATION.RESOLVE')
      || perms.includes('ADMIN.RECONCILIATION.MANAGE');
  }

  /**
   * WHY: Resolving exceptions (attaching settlement IDs, dismissing) uses
   * ADMIN.RECONCILIATION.RESOLVE. The Sales Verification queue is the
   * primary UI for this action for non-admin roles like MANAGER or CASHIER.
   */
  get canResolveException(): boolean {
    const perms = this.authService.currentUser?.permissions ?? [];
    return perms.includes('ADMIN.RECONCILIATION.RESOLVE')
      || perms.includes('ADMIN.RECONCILIATION.POS.RESOLVE')
      || perms.includes('ADMIN.RECONCILIATION.MANAGE');
  }

  ngOnInit(): void {
    this.loadEvents();
  }

  refresh(): void {
    if (this.loading) return;
    this.loadEvents();
  }

  applyFilters(): void {
    this.filteredEvents = this.allEvents.filter(e => {
      const sourceMatch = !this.sourceTypeFilter || e.sourceType === this.sourceTypeFilter;
      const typeMatch = !this.eventTypeFilter || e.eventType === this.eventTypeFilter;
      return sourceMatch && typeMatch;
    });
  }

  getEventTypeLabel(type: string): string {
    switch (type) {
      case 'MatchFound': return 'Match Found';
      case 'MatchNotFound': return 'No Match';
      case 'AmountMismatch': return 'Amount Mismatch';
      case 'Confirmed': return 'Confirmed';
      default: return type;
    }
  }

  getEventTypeClass(type: string): string {
    switch (type) {
      case 'MatchFound': return 'chip-matched';
      case 'Confirmed': return 'chip-confirmed';
      case 'MatchNotFound': return 'chip-exception';
      case 'AmountMismatch': return 'chip-warning';
      default: return 'chip-default';
    }
  }

  getStatusClass(status: string): string {
    switch (status) {
      case 'Resolved': return 'status-resolved';
      case 'Pending': return 'status-pending';
      default: return 'status-default';
    }
  }

  parseDetail(detailJson?: string | null): Record<string, unknown> | null {
    if (!detailJson) return null;
    try { return JSON.parse(detailJson) as Record<string, unknown>; }
    catch { return null; }
  }

  getUniqueSourceTypes(): string[] {
    return [...new Set(this.allEvents.map(e => e.sourceType))];
  }

  getUniqueEventTypes(): string[] {
    return [...new Set(this.allEvents.map(e => e.eventType))];
  }

  private loadEvents(): void {
    // WHY: Guard the API call client-side. The backend also enforces scope,
    // but failing silently here avoids a confusing 403 snackbar for VIEW-denied users.
    if (!this.canViewRecon) {
      this.allEvents = [];
      this.filteredEvents = [];
      return;
    }

    this.loading = true;
    // WHY: Stage filter is omitted here so that POS Level1 exceptions surface alongside
    // ERP Level3 exceptions — both require accountant review before proceeding downstream.
    this.reconciliationService.getEvents({ status: 'Pending' }).subscribe({
      next: (events) => {
        // Exclude Level4 bank events — those belong in the Matcher, not the Sales Verification queue
        this.allEvents = events.filter(e => e.stage !== 'Level4');
        this.applyFilters();
        this.loading = false;
      },
      error: (error: unknown) => {
        this.loading = false;
        this.snackBar.open(this.extractErrorMessage(error), 'Close', { duration: 3500 });
      },
    });
  }

  private extractErrorMessage(error: unknown): string {
    if (error instanceof HttpErrorResponse) {
      const body = error.error as { message?: string } | null;
      return body?.message ?? `Request failed with status ${error.status}.`;
    }
    return 'Request failed.';
  }
}
