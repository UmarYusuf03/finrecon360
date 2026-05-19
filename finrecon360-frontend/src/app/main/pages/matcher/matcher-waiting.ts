import { Component, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { HttpErrorResponse } from '@angular/common/http';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatDialogModule, MatDialog } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatSnackBar, MatSnackBarModule } from '@angular/material/snack-bar';
import { MatTableModule } from '@angular/material/table';
import { RouterLink, RouterLinkActive } from '@angular/router';

import { ReconciliationService } from '../../../core/admin-rbac/reconciliation.service';
import { AuthService } from '../../../core/auth/auth.service';
import { WaitingRecord } from '../../../core/admin-rbac/models';

@Component({
  selector: 'app-matcher-waiting',
  standalone: true,
  imports: [
    CommonModule,
    FormsModule,
    MatCardModule,
    MatTableModule,
    MatButtonModule,
    MatFormFieldModule,
    MatInputModule,
    MatIconModule,
    MatSnackBarModule,
    MatProgressSpinnerModule,
    MatDialogModule,
    RouterLink,
    RouterLinkActive,
  ],
  templateUrl: './matcher-waiting.html',
  styleUrls: ['./matcher-waiting.scss'],
})
export class MatcherWaitingComponent implements OnInit {
  displayedColumns = ['transactionDate', 'referenceNumber', 'description', 'grossAmount', 'processingFee', 'netAmount', 'actions'];
  records: WaitingRecord[] = [];
  loading = false;

  // Inline settlement ID attach
  attachingId: string | null = null;
  settlementIdInput = '';
  attachingInProgress = false;

  constructor(
    private reconciliationService: ReconciliationService,
    private authService: AuthService,
    private snackBar: MatSnackBar,
  ) {}

  private has(code: string): boolean {
    return this.authService.currentUser?.permissions.includes(code) ?? false;
  }

  // RESOLVE: covers attaching settlement IDs (PATCH /records/:id/settlement-id)
  // WHY: MANAGER gets RESOLVE without full MANAGE, so this check must include both.
  get canResolve(): boolean {
    return this.has('ADMIN.RECONCILIATION.RESOLVE')
        || this.has('ADMIN.RECONCILIATION.MANAGE');
  }
  get canViewRecon(): boolean {
    return this.has('ADMIN.RECONCILIATION.VIEW');
  }

  ngOnInit(): void {
    this.loadQueue();
  }

  refresh(): void {
    if (this.loading) return;
    this.loadQueue();
  }

  startAttach(record: WaitingRecord): void {
    this.attachingId = record.importedNormalizedRecordId;
    this.settlementIdInput = '';
  }

  cancelAttach(): void {
    this.attachingId = null;
    this.settlementIdInput = '';
  }

  submitAttach(record: WaitingRecord): void {
    if (!this.settlementIdInput.trim()) {
      this.snackBar.open('Settlement ID cannot be empty.', 'Close', { duration: 3000 });
      return;
    }

    this.attachingInProgress = true;
    this.reconciliationService.attachSettlementId(record.importedNormalizedRecordId, {
      settlementId: this.settlementIdInput.trim(),
    }).subscribe({
      next: () => {
        this.attachingInProgress = false;
        this.attachingId = null;
        this.settlementIdInput = '';
        this.snackBar.open('Settlement ID attached. Record requeued for Level-4 matching.', 'Close', { duration: 4000 });
        this.loadQueue();
      },
      error: (error: unknown) => {
        this.attachingInProgress = false;
        this.snackBar.open(this.extractErrorMessage(error), 'Close', { duration: 3500 });
      },
    });
  }

  private loadQueue(): void {
    this.loading = true;
    this.reconciliationService.getWaitingQueue().subscribe({
      next: (records) => {
        this.records = records;
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
