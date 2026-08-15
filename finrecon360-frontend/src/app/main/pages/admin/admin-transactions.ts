import { Component, OnInit, TemplateRef } from '@angular/core';
import { CommonModule } from '@angular/common';
import { HttpErrorResponse } from '@angular/common/http';
import {
  AbstractControl,
  FormBuilder,
  FormGroup,
  ReactiveFormsModule,
  ValidationErrors,
  ValidatorFn,
  Validators,
} from '@angular/forms';
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
import { RouterLink, RouterLinkActive } from '@angular/router';
import { Observable } from 'rxjs';

import { BankAccountService } from '../../../core/admin-rbac/bank-account.service';
import { TransactionService } from '../../../core/admin-rbac/transaction.service';
import {
  BankAccount,
  RejectTransactionRequest,
  Transaction,
  TransactionStateHistory,
  UpdateTransactionRequest,
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
    RouterLink,
    RouterLinkActive,
  ],
  templateUrl: './admin-transactions.html',
  styleUrls: ['./admin-transaction-pages.scss'],
})
export class AdminTransactionsComponent implements OnInit {
  displayedColumns = ['transactionDate', 'amount', 'type', 'method', 'bankAccount', 'state', 'actions'];
  historyColumns = ['changedAt', 'transition', 'changedBy', 'note'];
  transactions: Transaction[] = [];
  bankAccounts: BankAccount[] = [];
  history: TransactionStateHistory[] = [];
  form!: FormGroup;
  rejectForm!: FormGroup;
  editingTransaction: Transaction | null = null;
  rejectingTransaction: Transaction | null = null;
  historyTransaction: Transaction | null = null;
  historyError: string | null = null;
  loading = false;
  historyLoading = false;
  saving = false;
  actionId: string | null = null;
  saveError: string | null = null;

  readonly transactionTypes = ['CashIn', 'CashOut'];
  readonly paymentMethods = ['Cash', 'Card'];
  readonly minTransactionDate = '2000-01-01';
  readonly maxTransactionDate = new Date().toISOString().slice(0, 10);

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
      transactionDate: ['', [Validators.required, this.transactionDateValidator()]],
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
    this.editingTransaction = null;
    this.saveError = null;
    this.form.enable({ emitEvent: false });
    this.form.reset({
      amount: null,
      transactionDate: '',
      description: '',
      transactionType: 'CashIn',
      paymentMethod: 'Cash',
      bankAccountId: null,
    });
    this.updateBankAccountValidator();
    this.dialog.open(dialogTemplate, { width: '760px', maxWidth: '96vw' });
  }

  openEdit(transaction: Transaction, dialogTemplate: TemplateRef<unknown>): void {
    // Only Pending transactions can be modified.
    // Approved/Rejected transactions are locked for audit integrity.
    if (!this.canManageTransactions || !this.isPending(transaction) || this.isActionBusy(transaction)) {
      return;
    }

    this.editingTransaction = transaction;
    this.saveError = null;
    this.form.enable({ emitEvent: false });
    this.form.reset({
      amount: transaction.amount,
      transactionDate: transaction.transactionDate.slice(0, 10),
      description: transaction.description,
      transactionType: transaction.transactionType,
      paymentMethod: transaction.paymentMethod,
      bankAccountId: transaction.bankAccountId ?? null,
    });
    this.updateBankAccountValidator();
    this.dialog.open(dialogTemplate, { width: '760px', maxWidth: '96vw' });
  }

  save(): void {
    if (!this.canManageTransactions) {
      return;
    }

    // Prevent duplicate actions while request is in progress.
    // Ensures idempotent UI behavior.
    if (this.saving) {
      return;
    }

    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }

    const raw = this.form.getRawValue();
    const payload: UpdateTransactionRequest = {
      amount: Number(raw.amount),
      transactionDate: `${raw.transactionDate}T00:00:00`,
      description: raw.description,
      transactionType: raw.transactionType,
      paymentMethod: raw.paymentMethod,
      bankAccountId: raw.bankAccountId || null,
    };

    this.setSavingState(true);
    const successMessage = this.editingTransaction
      ? 'Transaction updated successfully.'
      : 'Transaction created successfully.';
    const request$: Observable<unknown> = this.editingTransaction
      ? this.transactionService.update(this.editingTransaction.transactionId, payload)
      : this.transactionService.create(payload);

    request$.subscribe({
      next: () => {
        this.setSavingState(false);
        this.dialog.closeAll();
        this.editingTransaction = null;
        this.loadTransactions();
        this.snackBar.open(successMessage, 'Close', { duration: 2500 });
      },
      error: (error: unknown) => {
        this.setSavingState(false);
        const message = this.extractErrorMessage(error);
        this.saveError = message;
        this.snackBar.open(message, 'Close', { duration: 3500 });
      },
    });
  }

  approve(transaction: Transaction): void {
    // Prevent duplicate actions while request is in progress.
    // Ensures idempotent UI behavior.
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
    // Only Pending transactions can be modified.
    // Approved/Rejected transactions are locked for audit integrity.
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
    this.historyError = null;
    this.historyLoading = true;
    this.dialog.open(dialogTemplate, { width: '860px', maxWidth: '96vw' });

    this.transactionService.getHistory(transaction.transactionId).subscribe({
      next: (history) => {
        this.history = history;
        this.historyLoading = false;
      },
      error: (error: unknown) => {
        this.historyLoading = false;
        this.historyError = this.extractErrorMessage(error);
        this.snackBar.open(this.historyError, 'Close', { duration: 3500 });
      },
    });
  }

  reject(): void {
    // Prevent duplicate actions while request is in progress.
    // Ensures idempotent UI behavior.
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

  getDialogTitle(): string {
    return this.editingTransaction ? 'Edit Transaction' : 'Add Transaction';
  }

  getDialogHelperText(): string {
    return this.editingTransaction
      ? 'Only pending transactions can be edited before approval.'
      : 'Create a new pending transaction for approval.';
  }

  getSaveButtonLabel(): string {
    if (this.saving) {
      return 'Saving...';
    }

    return this.editingTransaction ? 'Update Transaction' : 'Create Transaction';
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

  private setSavingState(isSaving: boolean): void {
    this.saving = isSaving;
    this.saveError = isSaving ? null : this.saveError;

    if (isSaving) {
      this.form.disable({ emitEvent: false });
      return;
    }

    this.form.enable({ emitEvent: false });
    this.updateBankAccountValidator();
  }

  private transactionDateValidator(): ValidatorFn {
    return (control: AbstractControl): ValidationErrors | null => {
      const value = control.value;
      if (!value) {
        return null;
      }

      if (typeof value !== 'string' || !/^\d{4}-\d{2}-\d{2}$/.test(value)) {
        return { invalidDate: true };
      }

      const parsed = new Date(`${value}T00:00:00`);
      if (Number.isNaN(parsed.getTime())) {
        return { invalidDate: true };
      }

      if (value < this.minTransactionDate) {
        return { minDate: true };
      }

      if (value > this.maxTransactionDate) {
        return { maxDate: true };
      }

      return null;
    };
  }

  private extractErrorMessage(error: unknown): string {
    if (error instanceof HttpErrorResponse) {
      if (typeof error.error === 'string' && error.error.trim()) {
        return error.error.trim();
      }

      const body = error.error as { message?: string; error?: { message?: string } } | null;
      return body?.message
        ?? body?.error?.message
        ?? error.message
        ?? `Request failed with status ${error.status}.`;
    }

    if (error instanceof Error && error.message) {
      return error.message;
    }

    return 'Request failed. Please review the form and try again.';
  }
}
