import { Component, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { HttpErrorResponse } from '@angular/common/http';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatSnackBar, MatSnackBarModule } from '@angular/material/snack-bar';
import { MatTableModule } from '@angular/material/table';
import { MatTooltipModule } from '@angular/material/tooltip';
import { MatExpansionModule } from '@angular/material/expansion';
import { RouterLink, RouterLinkActive } from '@angular/router';

import { BankAccountService } from '../../../core/admin-rbac/bank-account.service';
import { TransactionService } from '../../../core/admin-rbac/transaction.service';
import { BankAccount, NeedsBankMatchRecord } from '../../../core/admin-rbac/models';

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
    MatTooltipModule,
    MatExpansionModule,
    RouterLink,
    RouterLinkActive,
  ],
  templateUrl: './admin-needs-bank-match.html',
  styleUrls: ['./admin-transaction-pages.scss'],
})
export class AdminNeedsBankMatchComponent implements OnInit {
  readonly displayedColumns = [
    'transactionDate',
    'amount',
    'type',
    'bankAccount',
    'description',
    'importContext',
    'matchStatus',
    'matchGroup',
    'goToMatcher',
  ];

  records: NeedsBankMatchRecord[] = [];
  bankAccounts: BankAccount[] = [];
  loading = false;

  // Summary
  get withImportContext(): number { return this.records.filter(r => !!r.importedNormalizedRecordId).length; }
  get withMatchGroup(): number { return this.records.filter(r => !!r.reconciliationMatchGroupId).length; }
  get confirmedCount(): number { return this.records.filter(r => r.isConfirmed).length; }

  constructor(
    private transactionService: TransactionService,
    private bankAccountService: BankAccountService,
    private snackBar: MatSnackBar,
  ) {}

  ngOnInit(): void {
    this.loadBankAccounts();
    this.loadQueue();
  }

  refresh(): void {
    if (this.loading) return;
    this.loadQueue();
  }

  getBankAccountLabel(bankAccountId?: string | null): string {
    if (!bankAccountId) return '—';
    const account = this.bankAccounts.find(a => a.bankAccountId === bankAccountId);
    return account ? `${account.bankName} · ${account.accountNumber}` : bankAccountId;
  }

  getMatchStatusClass(status: string): string {
    switch (status) {
      case 'MATCHED':           return 'ms-matched';
      case 'INTERNAL_VERIFIED': return 'ms-verified';
      case 'SALES_VERIFIED':    return 'ms-verified';
      case 'EXCEPTION':         return 'ms-exception';
      case 'WAITING':           return 'ms-waiting';
      default:                  return 'ms-pending';
    }
  }

  getMatchLevelLabel(level?: string | null): string {
    switch (level) {
      case 'Level3': return 'L3 — Sales Match';
      case 'Level4': return 'L4 — Settlement';
      default: return level ?? '—';
    }
  }

  hasImportContext(record: NeedsBankMatchRecord): boolean {
    return !!record.importedNormalizedRecordId;
  }

  private loadQueue(): void {
    this.loading = true;
    this.transactionService.getNeedsBankMatch().subscribe({
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

  private loadBankAccounts(): void {
    this.bankAccountService.getAll().subscribe({
      next: (accounts) => { this.bankAccounts = accounts; },
      error: (error: unknown) => {
        this.snackBar.open(this.extractErrorMessage(error), 'Close', { duration: 3000 });
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
