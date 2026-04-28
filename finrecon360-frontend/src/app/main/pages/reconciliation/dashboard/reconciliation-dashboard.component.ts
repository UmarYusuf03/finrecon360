import { CommonModule } from '@angular/common';
import { Component, OnDestroy } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatOptionModule } from '@angular/material/core';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatSelectModule } from '@angular/material/select';
import { MatSnackBar, MatSnackBarModule } from '@angular/material/snack-bar';
import { RouterLink } from '@angular/router';
import { BehaviorSubject, finalize, Subject, takeUntil } from 'rxjs';

import {
  BankAccount,
  BankStatementImport,
  MatchingSummaryResponse,
} from '../../../../core/reconciliation/reconciliation.models';
import { ReconciliationService } from '../../../../core/reconciliation/reconciliation.service';

@Component({
  selector: 'app-reconciliation-dashboard',
  standalone: true,
  imports: [
    CommonModule,
    MatCardModule,
    MatButtonModule,
    MatSelectModule,
    MatOptionModule,
    MatFormFieldModule,
    MatProgressSpinnerModule,
    MatSnackBarModule,
    RouterLink,
  ],
  templateUrl: './reconciliation-dashboard.component.html',
  styleUrls: ['./reconciliation-dashboard.component.scss'],
})
export class ReconciliationDashboardComponent implements OnDestroy {
  private readonly destroy$ = new Subject<void>();

  readonly bankAccounts$ = new BehaviorSubject<BankAccount[]>([]);
  readonly isLoadingAccounts$ = new BehaviorSubject<boolean>(false);
  readonly imports$ = new BehaviorSubject<BankStatementImport[]>([]);
  readonly isLoadingImports$ = new BehaviorSubject<boolean>(false);

  readonly isProcessing$ = new BehaviorSubject<boolean>(false);
  readonly summary$ = new BehaviorSubject<MatchingSummaryResponse | null>(null);

  selectedBankAccountId = '';
  selectedImportId = '';

  constructor(
    private reconciliationService: ReconciliationService,
    private snackBar: MatSnackBar,
  ) {
    this.loadBankAccounts();
  }

  private loadBankAccounts(): void {
    this.isLoadingAccounts$.next(true);

    this.reconciliationService
      .getBankAccounts()
      .pipe(
        takeUntil(this.destroy$),
        finalize(() => this.isLoadingAccounts$.next(false)),
      )
      .subscribe({
        next: (accounts) => {
          this.bankAccounts$.next(accounts);
          const firstAccountId = accounts[0]?.id ?? '';
          this.selectedBankAccountId = firstAccountId;

          if (firstAccountId) {
            this.loadImports(firstAccountId);
          } else {
            this.imports$.next([]);
            this.selectedImportId = '';
          }
        },
        error: () => {
          this.bankAccounts$.next([]);
          this.selectedBankAccountId = '';
          this.imports$.next([]);
          this.selectedImportId = '';
          this.snackBar.open('Unable to load bank accounts.', 'Close', {
            duration: 3000,
            horizontalPosition: 'right',
            verticalPosition: 'top',
          });
        },
      });
  }

  onBankAccountChange(accountId: string): void {
    this.selectedBankAccountId = accountId;
    this.imports$.next([]);
    this.selectedImportId = '';

    if (!accountId) {
      return;
    }

    this.loadImports(accountId);
  }

  private loadImports(bankAccountId: string): void {
    this.isLoadingImports$.next(true);

    this.reconciliationService
      .getAvailableImports(bankAccountId)
      .pipe(
        takeUntil(this.destroy$),
        finalize(() => this.isLoadingImports$.next(false)),
      )
      .subscribe({
        next: (imports) => {
          this.imports$.next(imports);
          this.selectedImportId = imports[0]?.importId ?? '';
        },
        error: () => {
          this.imports$.next([]);
          this.selectedImportId = '';
          this.snackBar.open('Unable to load statement imports.', 'Close', {
            duration: 3000,
            horizontalPosition: 'right',
            verticalPosition: 'top',
          });
        },
      });
  }

  runMatching(): void {
    if (!this.selectedImportId || this.isProcessing$.value) {
      return;
    }

    this.isProcessing$.next(true);

    this.reconciliationService
      .runAutomatedMatching(this.selectedImportId)
      .pipe(
        takeUntil(this.destroy$),
        finalize(() => this.isProcessing$.next(false)),
      )
      .subscribe({
        next: (summary) => {
          this.summary$.next(summary);
          if (summary.matchesFound > 0) {
            this.snackBar.open('Matching Complete!', 'Close', {
              duration: 3000,
              horizontalPosition: 'right',
              verticalPosition: 'top',
              panelClass: ['recon-snackbar-success'],
            });
          }
        },
        error: () => {
          this.snackBar.open('Matching failed. Please try again.', 'Close', {
            duration: 4000,
            horizontalPosition: 'right',
            verticalPosition: 'top',
            panelClass: ['recon-snackbar-error'],
          });
        },
      });
  }

  trackById(_: number, item: BankStatementImport): string {
    return item.importId;
  }

  ngOnDestroy(): void {
    this.destroy$.next();
    this.destroy$.complete();
    this.bankAccounts$.complete();
    this.isLoadingAccounts$.complete();
    this.imports$.complete();
    this.isLoadingImports$.complete();
    this.isProcessing$.complete();
    this.summary$.complete();
  }
}
