import { Component, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { HttpErrorResponse } from '@angular/common/http';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatSnackBar, MatSnackBarModule } from '@angular/material/snack-bar';
import { MatTableModule } from '@angular/material/table';
import { RouterLink, RouterLinkActive } from '@angular/router';

import { BankAccountService } from '../../../core/admin-rbac/bank-account.service';
import { TransactionService } from '../../../core/admin-rbac/transaction.service';
import { BankAccount, Transaction } from '../../../core/admin-rbac/models';

@Component({
  selector: 'app-admin-needs-bank-match',
  standalone: true,
  imports: [
    CommonModule,
    MatCardModule,
    MatTableModule,
    MatButtonModule,
    MatIconModule,
    MatSnackBarModule,
    MatProgressSpinnerModule,
    RouterLink,
    RouterLinkActive,
  ],
  templateUrl: './admin-needs-bank-match.html',
  styleUrls: ['./admin-transaction-pages.scss'],
})
export class AdminNeedsBankMatchComponent implements OnInit {
  displayedColumns = ['transactionDate', 'amount', 'type', 'method', 'bankAccount', 'description', 'state'];
  transactions: Transaction[] = [];
  bankAccounts: BankAccount[] = [];
  loading = false;

  constructor(
    private transactionService: TransactionService,
    private bankAccountService: BankAccountService,
    private snackBar: MatSnackBar,
  ) {}

  ngOnInit(): void {
    this.loadBankAccounts();
    this.loadNeedsBankMatch();
  }

  refresh(): void {
    if (this.loading) {
      return;
    }

    this.loadNeedsBankMatch();
  }

  getBankAccountLabel(bankAccountId?: string | null): string {
    if (!bankAccountId) {
      return '-';
    }

    const account = this.bankAccounts.find((item) => item.bankAccountId === bankAccountId);
    return account ? `${account.bankName} - ${account.accountNumber}` : bankAccountId;
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
      case 'Pending':
        return 'state-pending';
      case 'JournalReady':
        return 'state-journal-ready';
      case 'NeedsBankMatch':
        return 'state-needs-bank-match';
      case 'Rejected':
        return 'state-rejected';
      default:
        return 'state-default';
    }
  }

  private loadNeedsBankMatch(): void {
    this.loading = true;
    // This queue is read-only until the matcher/reconciliation member owns the next handoff.
    this.transactionService.getNeedsBankMatch().subscribe({
      next: (transactions) => {
        this.transactions = transactions;
        this.loading = false;
      },
      error: (error: unknown) => {
        this.loading = false;
        this.snackBar.open(this.extractErrorMessage(error), 'Close', { duration: 3500 });
      },
    });
  }

  private loadBankAccounts(): void {
    this.bankAccountService.getAll().subscribe({
      next: (accounts) => {
        this.bankAccounts = accounts;
      },
      error: (error: unknown) => {
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
