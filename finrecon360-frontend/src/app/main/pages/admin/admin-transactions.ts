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
})
export class AdminTransactionsComponent implements OnInit {
  displayedColumns = ['transactionDate', 'amount', 'type', 'method', 'bankAccount', 'state', 'actions'];
  transactions: Transaction[] = [];
  bankAccounts: BankAccount[] = [];
  form!: FormGroup;
  rejectForm!: FormGroup;
  rejectingTransaction: Transaction | null = null;
  loading = false;
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
    if (transaction.transactionState !== 'Pending' || this.actionId) {
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
    this.rejectingTransaction = transaction;
    this.rejectForm.reset({ reason: '' });
    this.dialog.open(dialogTemplate);
  }

  reject(): void {
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
