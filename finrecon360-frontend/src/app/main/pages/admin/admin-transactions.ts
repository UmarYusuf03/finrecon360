import { Component, OnInit, TemplateRef } from '@angular/core';
import { CommonModule } from '@angular/common';
import { HttpErrorResponse } from '@angular/common/http';
import { FormBuilder, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatDialog, MatDialogModule } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatSelectModule } from '@angular/material/select';
import { MatSnackBar, MatSnackBarModule } from '@angular/material/snack-bar';
import { MatTableModule } from '@angular/material/table';

import { BankAccountService } from '../../../core/admin-rbac/bank-account.service';
import { TransactionService } from '../../../core/admin-rbac/transaction.service';
import {
  BankAccount,
  CreateTransactionRequest,
  RejectTransactionRequest,
  Transaction,
  TransactionStateHistory,
} from '../../../core/admin-rbac/models';
import { AuthService } from '../../../core/auth/auth.service';

@Component({
  selector: 'app-admin-transactions',
  standalone: true,
  imports: [
    CommonModule,
    ReactiveFormsModule,
    MatCardModule,
    MatTableModule,
    MatButtonModule,
    MatIconModule,
    MatDialogModule,
    MatFormFieldModule,
    MatInputModule,
    MatSelectModule,
    MatSnackBarModule,
    MatProgressSpinnerModule,
  ],
  templateUrl: './admin-transactions.html',
  styles: [`
    .transactions-card {
      padding: 4px;
    }

    .transactions-header {
      display: flex;
      align-items: flex-start;
      justify-content: space-between;
      gap: 16px;
      margin-bottom: 18px;
    }

    .transactions-header h2 {
      margin: 0 0 4px;
    }

    .transactions-header p {
      margin: 0;
      color: rgba(0, 0, 0, 0.62);
    }

    .transactions-header__actions,
    .transaction-actions {
      display: flex;
      align-items: center;
      gap: 8px;
    }

    .transactions-header__actions button {
      min-height: 40px;
    }

    .transactions-header__actions mat-icon {
      margin-right: 6px;
    }

    .transactions-loading {
      display: flex;
      justify-content: center;
      padding: 32px 24px;
    }

    .transactions-table {
      width: 100%;
      overflow: hidden;
    }

    .amount-cell {
      font-variant-numeric: tabular-nums;
      font-weight: 600;
    }

    .bank-account-label {
      color: rgba(0, 0, 0, 0.72);
    }

    .state-badge {
      display: inline-flex;
      align-items: center;
      border-radius: 999px;
      padding: 4px 10px;
      font-size: 12px;
      font-weight: 700;
      line-height: 1.2;
      white-space: nowrap;
    }

    .state-pending {
      background: #fff7ed;
      color: #9a3412;
    }

    .state-journal-ready {
      background: #ecfdf5;
      color: #047857;
    }

    .state-needs-bank-match {
      background: #eff6ff;
      color: #1d4ed8;
    }

    .state-rejected {
      background: #fef2f2;
      color: #b91c1c;
    }

    .state-default {
      background: #f3f4f6;
      color: #374151;
    }

    .action-button {
      color: rgba(0, 0, 0, 0.64);
    }

    .action-history {
      color: #475569;
    }

    .action-approve:not(:disabled) {
      color: #047857;
    }

    .action-reject:not(:disabled) {
      color: #b91c1c;
    }

    .action-button:disabled {
      color: rgba(0, 0, 0, 0.28);
      background: rgba(0, 0, 0, 0.04);
    }

    .transactions-empty {
      display: grid;
      justify-items: center;
      gap: 6px;
      padding: 40px 16px;
      text-align: center;
      color: rgba(0, 0, 0, 0.62);
    }

    .transactions-empty mat-icon {
      width: 40px;
      height: 40px;
      font-size: 40px;
      color: rgba(0, 0, 0, 0.38);
    }

    .transactions-empty h3 {
      margin: 8px 0 0;
      color: rgba(0, 0, 0, 0.82);
    }

    .transactions-empty p {
      margin: 0;
    }

    .transaction-dialog {
      min-width: min(640px, 82vw);
    }

    .transaction-form {
      display: flex;
      flex-direction: column;
      gap: 14px;
    }

    .transaction-form__grid {
      display: grid;
      grid-template-columns: repeat(2, minmax(0, 1fr));
      gap: 14px;
    }

    [mat-dialog-actions] button mat-spinner {
      display: inline-block;
      margin-right: 8px;
      vertical-align: middle;
    }

    @media (max-width: 720px) {
      .transactions-header {
        align-items: stretch;
        flex-direction: column;
      }

      .transactions-header__actions {
        justify-content: flex-start;
        flex-wrap: wrap;
      }

      .transaction-form__grid {
        grid-template-columns: 1fr;
      }
    }
  `],
})
export class AdminTransactionsComponent implements OnInit {
  displayedColumns = ['transactionDate', 'amount', 'type', 'method', 'bankAccount', 'state', 'actions'];
  historyColumns = ['changedAt', 'transition', 'changedBy', 'note'];
  transactions: Transaction[] = [];
  bankAccounts: BankAccount[] = [];
  history: TransactionStateHistory[] = [];
  form!: FormGroup;
  rejectForm!: FormGroup;
  rejectingTransaction: Transaction | null = null;
  historyTransaction: Transaction | null = null;
  loading = false;
  historyLoading = false;
  saving = false;
  actionId: string | null = null;
  saveError: string | null = null;

  readonly transactionTypes = ['CashIn', 'CashOut'];
  readonly paymentMethods = ['Cash', 'Card'];

  constructor(
    private transactionService: TransactionService,
    private bankAccountService: BankAccountService,
    private authService: AuthService,
    private fb: FormBuilder,
    private dialog: MatDialog,
    private snackBar: MatSnackBar,
  ) {}

  ngOnInit(): void {
    this.form = this.fb.group({
      amount: [null, [Validators.required, Validators.min(0.01)]],
      transactionDate: ['', Validators.required],
      description: ['', [Validators.required, Validators.maxLength(500)]],
      transactionType: ['CashIn', Validators.required],
      paymentMethod: ['Cash', Validators.required],
      bankAccountId: [null],
    });
    this.rejectForm = this.fb.group({
      reason: ['', [Validators.required, Validators.maxLength(500)]],
    });

    this.form.get('paymentMethod')?.valueChanges.subscribe(() => this.updateBankAccountValidator());
    this.updateBankAccountValidator();
    this.loadBankAccounts();
    this.loadTransactions();
  }

  get canManageTransactions(): boolean {
    return this.authService.currentUser?.permissions.includes('ADMIN.TRANSACTIONS.MANAGE') ?? false;
  }

  refresh(): void {
    if (this.loading) {
      return;
    }

    this.loadTransactions();
  }

  openAdd(dialogTemplate: TemplateRef<unknown>): void {
    this.saveError = null;
    this.form.reset({
      amount: null,
      transactionDate: '',
      description: '',
      transactionType: 'CashIn',
      paymentMethod: 'Cash',
      bankAccountId: null,
    });
    this.updateBankAccountValidator();
    this.dialog.open(dialogTemplate);
  }

  save(): void {
    if (this.saving) {
      return;
    }

    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }

    const raw = this.form.getRawValue();
    const payload: CreateTransactionRequest = {
      amount: Number(raw.amount),
      transactionDate: `${raw.transactionDate}T00:00:00`,
      description: raw.description,
      transactionType: raw.transactionType,
      paymentMethod: raw.paymentMethod,
      bankAccountId: raw.bankAccountId || null,
    };

    this.saving = true;
    this.saveError = null;
    this.transactionService.create(payload).subscribe({
      next: () => {
        this.saving = false;
        this.dialog.closeAll();
        this.loadTransactions();
        this.snackBar.open('Transaction created successfully.', 'Close', { duration: 2500 });
      },
      error: (error: unknown) => {
        this.saving = false;
        const message = this.extractErrorMessage(error);
        this.saveError = message;
        this.snackBar.open(message, 'Close', { duration: 3500 });
      },
    });
  }

  approve(transaction: Transaction): void {
    if (!this.canManageTransactions || !this.isPending(transaction) || this.actionId) {
      return;
    }

    this.actionId = transaction.transactionId;
    this.transactionService.approve(transaction.transactionId, {}).subscribe({
      next: () => {
        this.actionId = null;
        this.loadTransactions();
        this.snackBar.open('Transaction approved.', 'Close', { duration: 2500 });
      },
      error: (error: unknown) => {
        this.actionId = null;
        this.snackBar.open(this.extractErrorMessage(error), 'Close', { duration: 3500 });
      },
    });
  }

  openReject(transaction: Transaction, dialogTemplate: TemplateRef<unknown>): void {
    if (!this.canManageTransactions || !this.isPending(transaction) || this.actionId) {
      return;
    }

    this.rejectingTransaction = transaction;
    this.rejectForm.reset({ reason: '' });
    this.dialog.open(dialogTemplate);
  }

  openHistory(transaction: Transaction, dialogTemplate: TemplateRef<unknown>): void {
    this.historyTransaction = transaction;
    this.history = [];
    this.historyLoading = true;
    this.dialog.open(dialogTemplate, { width: '720px' });

    this.transactionService.getHistory(transaction.transactionId).subscribe({
      next: (history) => {
        this.history = history;
        this.historyLoading = false;
      },
      error: (error: unknown) => {
        this.historyLoading = false;
        this.snackBar.open(this.extractErrorMessage(error), 'Close', { duration: 3500 });
      },
    });
  }

  reject(): void {
    if (this.actionId) {
      return;
    }

    if (!this.rejectingTransaction || this.rejectForm.invalid) {
      this.rejectForm.markAllAsTouched();
      return;
    }

    const payload = this.rejectForm.getRawValue() as RejectTransactionRequest;
    this.actionId = this.rejectingTransaction.transactionId;
    this.transactionService.reject(this.rejectingTransaction.transactionId, payload).subscribe({
      next: () => {
        this.actionId = null;
        this.dialog.closeAll();
        this.rejectingTransaction = null;
        this.loadTransactions();
        this.snackBar.open('Transaction rejected.', 'Close', { duration: 2500 });
      },
      error: (error: unknown) => {
        this.actionId = null;
        this.snackBar.open(this.extractErrorMessage(error), 'Close', { duration: 3500 });
      },
    });
  }

  getBankAccountLabel(bankAccountId?: string | null): string {
    if (!bankAccountId) {
      return '-';
    }

    const account = this.bankAccounts.find((item) => item.bankAccountId === bankAccountId);
    return account ? `${account.bankName} - ${account.accountNumber}` : bankAccountId;
  }

  isPending(transaction: Transaction): boolean {
    return transaction.transactionState === 'Pending';
  }

  isActionBusy(transaction: Transaction): boolean {
    return this.actionId === transaction.transactionId;
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

  private loadTransactions(): void {
    this.loading = true;
    this.transactionService.getAll().subscribe({
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
        this.bankAccounts = accounts.filter((account) => account.isActive);
      },
      error: (error: unknown) => {
        this.snackBar.open(this.extractErrorMessage(error), 'Close', { duration: 3500 });
      },
    });
  }

  private updateBankAccountValidator(): void {
    const bankAccountControl = this.form.get('bankAccountId');
    if (!bankAccountControl) {
      return;
    }

    if (this.form.get('paymentMethod')?.value === 'Card') {
      // Backend requires card transactions to be tied to a bank account for later matching.
      bankAccountControl.setValidators([Validators.required]);
    } else {
      bankAccountControl.clearValidators();
    }

    bankAccountControl.updateValueAndValidity({ emitEvent: false });
  }

  private extractErrorMessage(error: unknown): string {
    if (error instanceof HttpErrorResponse) {
      const body = error.error as { message?: string } | null;
      return body?.message ?? `Request failed with status ${error.status}.`;
    }

    return 'Request failed.';
  }
}
