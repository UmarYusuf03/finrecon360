import { Component, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { HttpErrorResponse } from '@angular/common/http';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatSnackBar, MatSnackBarModule } from '@angular/material/snack-bar';
import { MatTableModule } from '@angular/material/table';

import { TransactionService } from '../../../core/admin-rbac/transaction.service';
import { Transaction } from '../../../core/admin-rbac/models';

@Component({
  selector: 'app-admin-journal-ready',
  standalone: true,
  imports: [
    CommonModule,
    MatCardModule,
    MatTableModule,
    MatButtonModule,
    MatSnackBarModule,
    MatProgressSpinnerModule,
  ],
  templateUrl: './admin-journal-ready.html',
})
export class AdminJournalReadyComponent implements OnInit {
  displayedColumns = ['transactionDate', 'amount', 'type', 'method', 'description', 'createdAt'];
  transactions: Transaction[] = [];
  loading = false;

  constructor(
    private transactionService: TransactionService,
    private snackBar: MatSnackBar,
  ) {}

  ngOnInit(): void {
    this.loadJournalReady();
  }

  refresh(): void {
    this.loadJournalReady();
  }

  private loadJournalReady(): void {
    this.loading = true;
    this.transactionService.getJournalReady().subscribe({
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

  private extractErrorMessage(error: unknown): string {
    if (error instanceof HttpErrorResponse) {
      const body = error.error as { message?: string } | null;
      return body?.message ?? `Request failed with status ${error.status}.`;
    }

    return 'Request failed.';
  }
}
