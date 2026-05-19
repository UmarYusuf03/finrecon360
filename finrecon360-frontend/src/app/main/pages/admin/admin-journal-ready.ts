import { Component, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { HttpErrorResponse } from '@angular/common/http';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatSelectModule } from '@angular/material/select';
import { MatSnackBar, MatSnackBarModule } from '@angular/material/snack-bar';
import { MatTableModule } from '@angular/material/table';
import { RouterLink, RouterLinkActive } from '@angular/router';

import { TransactionService } from '../../../core/admin-rbac/transaction.service';
import { ReconciliationService } from '../../../core/admin-rbac/reconciliation.service';
import { AuthService } from '../../../core/auth/auth.service';
import { Transaction } from '../../../core/admin-rbac/models';

@Component({
  selector: 'app-admin-journal-ready',
  standalone: true,
  imports: [
    CommonModule,
    FormsModule,
    MatCardModule,
    MatTableModule,
    MatButtonModule,
    MatFormFieldModule,
    MatIconModule,
    MatSelectModule,
    MatSnackBarModule,
    MatProgressSpinnerModule,
    RouterLink,
    RouterLinkActive,
  ],
  templateUrl: './admin-journal-ready.html',
  styleUrls: ['./admin-transaction-pages.scss'],
})
export class AdminJournalReadyComponent implements OnInit {
  displayedColumns = ['transactionDate', 'amount', 'type', 'method', 'state', 'description', 'createdAt', 'actions'];
  allTransactions: Transaction[] = [];
  transactions: Transaction[] = [];
  years: number[] = [];
  loading = false;
  selectedYear: number | null = null;
  selectedMonth: number | null = null;
  sortField: 'transactionDate' | 'amount' = 'transactionDate';
  sortDirection: 'asc' | 'desc' = 'asc';
  postingId: string | null = null;

  readonly months = [
    { value: 1, label: 'January' },
    { value: 2, label: 'February' },
    { value: 3, label: 'March' },
    { value: 4, label: 'April' },
    { value: 5, label: 'May' },
    { value: 6, label: 'June' },
    { value: 7, label: 'July' },
    { value: 8, label: 'August' },
    { value: 9, label: 'September' },
    { value: 10, label: 'October' },
    { value: 11, label: 'November' },
    { value: 12, label: 'December' },
  ];

  constructor(
    private transactionService: TransactionService,
    private reconciliationService: ReconciliationService,
    private authService: AuthService,
    private snackBar: MatSnackBar,
  ) {}

  get canPostJournal(): boolean {
    // WHY: ADMIN.JOURNAL.POST is the granular code from the rbac_granular_plan.
    // ADMIN.JOURNAL.MANAGE is kept as a legacy alias for existing DB grants.
    // The AliasMap in PermissionHandler means MANAGE→POST is implied on the backend,
    // but the frontend must check both so that roles seeded with only JOURNAL.POST
    // (not MANAGE) still get the button displayed.
    const perms = this.authService.currentUser?.permissions ?? [];
    return perms.includes('ADMIN.JOURNAL.POST') || perms.includes('ADMIN.JOURNAL.MANAGE');
  }

  ngOnInit(): void {
    this.loadJournalReady();
  }

  refresh(): void {
    if (this.loading) {
      return;
    }

    this.loadJournalReady();
  }

  // Client-side filtering applied on journal-ready dataset.
  // Backend filtering can be added later if needed.
  applyFilters(): void {
    let items = [...this.allTransactions];

    if (this.selectedYear) {
      items = items.filter((transaction) =>
        new Date(transaction.transactionDate).getFullYear() === this.selectedYear,
      );
    }

    if (this.selectedMonth) {
      items = items.filter((transaction) =>
        new Date(transaction.transactionDate).getMonth() + 1 === this.selectedMonth,
      );
    }

    items.sort((left, right) => {
      const comparison = this.sortField === 'amount'
        ? left.amount - right.amount
        : new Date(left.transactionDate).getTime() - new Date(right.transactionDate).getTime();

      return this.sortDirection === 'asc' ? comparison : -comparison;
    });

    this.transactions = items;
  }

  clearFilters(): void {
    this.selectedYear = null;
    this.selectedMonth = null;
    this.sortField = 'transactionDate';
    this.sortDirection = 'asc';
    this.applyFilters();
  }

  hasFilterState(): boolean {
    return this.selectedYear !== null ||
      this.selectedMonth !== null ||
      this.sortField !== 'transactionDate' ||
      this.sortDirection !== 'asc';
  }

  getStateLabel(state: string): string {
    switch (state) {
      case 'JournalReady':
        return 'Journal Ready';
      case 'NeedsBankMatch':
        return 'Needs Bank Match';
      default:
        return state;
    }
  }

  getStateClass(state: string): string {
    switch (state) {
      case 'Pending': return 'state-pending';
      case 'JournalReady': return 'state-journal-ready';
      case 'NeedsBankMatch': return 'state-needs-bank-match';
      case 'Rejected': return 'state-rejected';
      default: return 'state-default';
    }
  }

  postJournal(transaction: Transaction): void {
    if (this.postingId || !this.canPostJournal) return;
    this.postingId = transaction.transactionId;
    this.reconciliationService.postJournalFromTransaction(transaction.transactionId, {}).subscribe({
      next: () => {
        this.postingId = null;
        this.snackBar.open('Journal entry posted successfully.', 'Close', { duration: 4000 });
        this.loadJournalReady();
      },
      error: (error: unknown) => {
        this.postingId = null;
        this.snackBar.open(this.extractErrorMessage(error), 'Close', { duration: 3500 });
      },
    });
  }

  private loadJournalReady(): void {
    this.loading = true;
    // The backend only returns JournalReady items; NeedsBankMatch remains outside this queue.
    this.transactionService.getJournalReady().subscribe({
      next: (transactions) => {
        this.allTransactions = transactions;
        this.years = Array.from(
          new Set(transactions.map((transaction) => new Date(transaction.transactionDate).getFullYear())),
        ).sort((left, right) => right - left);
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
