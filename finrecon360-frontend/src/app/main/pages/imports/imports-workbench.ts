import { CommonModule } from '@angular/common';
import { HttpErrorResponse } from '@angular/common/http';
import { Component, OnInit } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Router } from '@angular/router';

import { AuthService } from '../../../core/auth/auth.service';
import { ImportsService } from '../../../core/imports/imports.service';
import {
  ImportHistoryItem,
  ImportParseResponse,
  ImportValidateResponse,
} from '../../../core/imports/imports.models';

@Component({
  selector: 'app-imports-workbench',
  standalone: true,
  imports: [CommonModule, FormsModule],
  templateUrl: './imports-workbench.html',
  styleUrls: ['./imports-workbench.scss'],
})
export class ImportsWorkbenchComponent implements OnInit {
  readonly canonicalFields = [
    'TransactionDate',
    'PostingDate',
    'ReferenceNumber',
    'Description',
    'AccountCode',
    'AccountName',
    'DebitAmount',
    'CreditAmount',
    'NetAmount',
    'Currency',
  ];

  loading = false;
  processing = false;
  actionMessage: string | null = null;
  actionError: string | null = null;

  sourceType = 'CSV';
  selectedFile: File | null = null;

  history: ImportHistoryItem[] = [];
  selectedBatch: ImportHistoryItem | null = null;
  search = '';
  statusFilter = '';

  parseResult: ImportParseResponse | null = null;
  validateResult: ImportValidateResponse | null = null;

  mapping: Record<string, string> = {};

  canManage = false;
  private authRetryInProgress = false;
  deleteDialogOpen = false;
  deleteTarget: ImportHistoryItem | null = null;

  constructor(
    private readonly importsService: ImportsService,
    private readonly authService: AuthService,
    private readonly router: Router,
  ) {}

  ngOnInit(): void {
    const user = this.authService.currentUser;
    const permissions = user?.permissions ?? [];
    this.canManage = permissions.includes('ADMIN.IMPORT_ARCHITECTURE.MANAGE');
    this.refreshHistory();
  }

  onFileSelected(event: Event): void {
    const input = event.target as HTMLInputElement;
    this.selectedFile = input.files?.item(0) ?? null;
  }

  upload(): void {
    if (!this.selectedFile) {
      this.actionError = 'Select a CSV or XLSX file first.';
      return;
    }

    this.processing = true;
    this.clearAlerts();
    this.importsService.uploadImport(this.selectedFile, this.sourceType).subscribe({
      next: (result) => {
        this.processing = false;
        this.actionMessage = `Upload created: ${result.id}`;
        this.selectedFile = null;
        this.refreshHistory();
      },
      error: (error) => {
        this.processing = false;
        if (this.tryRefreshSessionAndRetry(error, () => this.upload())) {
          return;
        }
        this.actionError = this.getErrorMessage(error, 'Upload failed.');
      },
    });
  }

  refreshHistory(): void {
    this.loading = true;
    this.importsService
      .getImportHistory({
        search: this.search || undefined,
        status: this.statusFilter || undefined,
        page: 1,
        pageSize: 100,
      })
      .subscribe({
        next: (res) => {
          this.loading = false;
          this.history = res.items;
          if (this.selectedBatch) {
            this.selectedBatch =
              this.history.find((h) => h.id === this.selectedBatch?.id) ?? this.selectedBatch;
          }
        },
        error: (error) => {
          this.loading = false;
          if (this.tryRefreshSessionAndRetry(error, () => this.refreshHistory())) {
            return;
          }
          this.actionError = this.getErrorMessage(error, 'Unable to load import history.');
        },
      });
  }

  selectBatch(item: ImportHistoryItem): void {
    this.selectedBatch = item;
    this.parseResult = null;
    this.validateResult = null;
    this.mapping = {};
    this.clearAlerts();
  }

  parseSelected(): void {
    if (!this.selectedBatch) return;

    this.processing = true;
    this.clearAlerts();
    this.importsService.parseImport(this.selectedBatch.id).subscribe({
      next: (res) => {
        this.processing = false;
        this.parseResult = res;
        this.validateResult = null;
        this.mapping = {};
        this.canonicalFields.forEach((field) => {
          this.mapping[field] = '';
        });
        this.actionMessage = 'File parsed. Map source headers to canonical fields.';
        this.refreshHistory();
      },
      error: (error) => {
        this.processing = false;
        if (this.tryRefreshSessionAndRetry(error, () => this.parseSelected())) {
          return;
        }
        this.actionError = this.getErrorMessage(error, 'Parse failed.');
      },
    });
  }

  saveMapping(): void {
    if (!this.selectedBatch) return;

    this.processing = true;
    this.clearAlerts();
    this.importsService
      .saveMapping(this.selectedBatch.id, {
        canonicalSchemaVersion: 'v1',
        fieldMappings: this.mapping,
      })
      .subscribe({
        next: () => {
          this.processing = false;
          this.actionMessage = 'Mapping saved.';
          this.refreshHistory();
        },
        error: (error) => {
          this.processing = false;
          if (this.tryRefreshSessionAndRetry(error, () => this.saveMapping())) {
            return;
          }
          this.actionError = this.getErrorMessage(error, 'Saving mapping failed.');
        },
      });
  }

  validateSelected(): void {
    if (!this.selectedBatch) return;

    this.processing = true;
    this.clearAlerts();
    this.importsService.validateImport(this.selectedBatch.id).subscribe({
      next: (res) => {
        this.processing = false;
        this.validateResult = res;
        this.actionMessage = `Validation done: ${res.validRows} valid, ${res.invalidRows} invalid.`;
        this.refreshHistory();
      },
      error: (error) => {
        this.processing = false;
        if (this.tryRefreshSessionAndRetry(error, () => this.validateSelected())) {
          return;
        }
        this.actionError = this.getErrorMessage(error, 'Validation failed.');
      },
    });
  }

  commitSelected(): void {
    if (!this.selectedBatch) return;

    this.processing = true;
    this.clearAlerts();
    this.importsService.commitImport(this.selectedBatch.id).subscribe({
      next: (res) => {
        this.processing = false;
        this.actionMessage = `Commit complete. ${res.normalizedCount} normalized rows created.`;
        this.refreshHistory();
      },
      error: (error) => {
        this.processing = false;
        if (this.tryRefreshSessionAndRetry(error, () => this.commitSelected())) {
          return;
        }
        this.actionError = this.getErrorMessage(error, 'Commit failed.');
      },
    });
  }

  deleteBatch(item: ImportHistoryItem, event?: Event): void {
    event?.stopPropagation();
    if (!this.canManage) {
      this.actionError = 'Only tenant admins can delete imports.';
      return;
    }

    this.deleteTarget = item;
    this.deleteDialogOpen = true;
  }

  confirmDeleteBatch(): void {
    if (!this.deleteTarget) {
      this.deleteDialogOpen = false;
      return;
    }

    const target = this.deleteTarget;
    this.deleteDialogOpen = false;
    this.deleteTarget = null;

    this.processing = true;
    this.clearAlerts();
    this.importsService.deleteImport(target.id).subscribe({
      next: () => {
        this.processing = false;
        if (this.selectedBatch?.id === target.id) {
          this.selectedBatch = null;
          this.parseResult = null;
          this.validateResult = null;
          this.mapping = {};
        }
        this.actionMessage = 'Import batch deleted.';
        this.refreshHistory();
      },
      error: (error) => {
        this.processing = false;
        if (this.tryRefreshSessionAndRetry(error, () => this.confirmDeleteBatch())) {
          return;
        }
        this.actionError = this.getErrorMessage(error, 'Delete failed.');
      },
    });
  }

  closeDeleteDialog(): void {
    this.deleteDialogOpen = false;
    this.deleteTarget = null;
  }

  private clearAlerts(): void {
    this.actionMessage = null;
    this.actionError = null;
  }

  private tryRefreshSessionAndRetry(error: unknown, retryAction: () => void): boolean {
    if (!(error instanceof HttpErrorResponse) || error.status !== 401 || this.authRetryInProgress) {
      return false;
    }

    this.authRetryInProgress = true;
    this.authService.refreshCurrentUser().subscribe({
      next: () => {
        this.authRetryInProgress = false;
        retryAction();
      },
      error: () => {
        this.authRetryInProgress = false;
        this.actionError = 'Session expired. Please sign in again.';
        this.authService.logout();
        void this.router.navigateByUrl('/auth/login');
      },
    });

    return true;
  }

  private getErrorMessage(error: unknown, fallback: string): string {
    if (!(error instanceof HttpErrorResponse)) {
      return fallback;
    }

    if (error.status === 0) {
      return 'Cannot reach backend API. Ensure the backend is running and API URL is correct.';
    }

    if (error.status === 401) {
      return 'Session expired. Please sign in again.';
    }

    if (error.status === 403) {
      return 'Access denied for this tenant/workspace. Use a tenant account with active membership.';
    }

    const body = error.error as { message?: string } | string | null;
    if (typeof body === 'string' && body.trim()) {
      return body;
    }

    if (
      body &&
      typeof body === 'object' &&
      typeof body.message === 'string' &&
      body.message.trim()
    ) {
      return body.message;
    }

    if (error.message?.trim()) {
      return error.message;
    }

    return fallback;
  }
}
