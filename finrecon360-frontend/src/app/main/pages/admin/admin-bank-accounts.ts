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
import { MatSnackBar, MatSnackBarModule } from '@angular/material/snack-bar';
import { MatTableModule } from '@angular/material/table';
import { Observable } from 'rxjs';

import { BankAccountService } from '../../../core/admin-rbac/bank-account.service';
import { BankAccount } from '../../../core/admin-rbac/models';

@Component({
  selector: 'app-admin-bank-accounts',
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
    MatSnackBarModule,
    MatProgressSpinnerModule,
  ],
  templateUrl: './admin-bank-accounts.html',
  styleUrls: ['./admin-bank-accounts.scss'],
})
export class AdminBankAccountsComponent implements OnInit {
  displayedColumns = ['bankName', 'accountName', 'accountNumber', 'currency', 'status', 'actions'];
  bankAccounts: BankAccount[] = [];
  form!: FormGroup;
  editingId: string | null = null;
  loading = false;
  saving = false;
  deactivatingId: string | null = null;
  saveError: string | null = null;

  constructor(
    private bankAccountService: BankAccountService,
    private fb: FormBuilder,
    private dialog: MatDialog,
    private snackBar: MatSnackBar,
  ) {}

  ngOnInit(): void {
    this.form = this.fb.group({
      bankName: ['', [Validators.required, Validators.maxLength(200)]],
      accountName: ['', [Validators.required, Validators.maxLength(200)]],
      accountNumber: ['', [Validators.required, Validators.maxLength(100)]],
      currency: ['', [Validators.required, Validators.maxLength(10)]],
    });

    this.loadBankAccounts();
  }

  refresh(): void {
    this.loadBankAccounts();
  }

  openAdd(dialogTemplate: TemplateRef<unknown>): void {
    this.editingId = null;
    this.saveError = null;
    this.form.reset();
    this.dialog.open(dialogTemplate);
  }

  openEdit(account: BankAccount, dialogTemplate: TemplateRef<unknown>): void {
    this.saveError = null;
    this.bankAccountService.getById(account.bankAccountId).subscribe({
      next: (details) => {
        this.editingId = details.bankAccountId;
        this.form.setValue({
          bankName: details.bankName,
          accountName: details.accountName,
          accountNumber: details.accountNumber,
          currency: details.currency,
        });
        this.dialog.open(dialogTemplate);
      },
      error: (error: unknown) => {
        this.snackBar.open(this.extractErrorMessage(error), 'Close', { duration: 3500 });
      },
    });
  }

  save(): void {
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }

    this.saving = true;
    this.saveError = null;

    const payload = this.form.getRawValue() as {
      bankName: string;
      accountName: string;
      accountNumber: string;
      currency: string;
    };

    const request$: Observable<unknown> = this.editingId
      ? this.bankAccountService.update(this.editingId, payload)
      : this.bankAccountService.create(payload);

    request$.subscribe({
      next: () => {
        this.saving = false;
        this.dialog.closeAll();
        this.loadBankAccounts();
        this.snackBar.open(
          this.editingId
            ? 'Bank account updated successfully.'
            : 'Bank account created successfully.',
          'Close',
          { duration: 2500 },
        );
      },
      error: (error: unknown) => {
        this.saving = false;
        const message = this.extractErrorMessage(error);
        this.saveError = message;
        this.snackBar.open(message, 'Close', { duration: 3500 });
      },
    });
  }

  deactivate(account: BankAccount): void {
    if (!account.isActive || this.deactivatingId) {
      return;
    }

    this.deactivatingId = account.bankAccountId;
    this.bankAccountService.deactivate(account.bankAccountId).subscribe({
      next: () => {
        this.deactivatingId = null;
        this.bankAccounts = this.bankAccounts.map((item) =>
          item.bankAccountId === account.bankAccountId
            ? { ...item, isActive: false, updatedAt: new Date().toISOString() }
            : item,
        );
        this.snackBar.open('Bank account deactivated.', 'Close', { duration: 2500 });
      },
      error: (error: unknown) => {
        this.deactivatingId = null;
        this.snackBar.open(this.extractErrorMessage(error), 'Close', { duration: 3500 });
      },
    });
  }

  private loadBankAccounts(): void {
    this.loading = true;
    this.bankAccountService.getAll().subscribe({
      next: (accounts) => {
        this.bankAccounts = [...accounts].sort((left, right) =>
          right.createdAt.localeCompare(left.createdAt),
        );
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
