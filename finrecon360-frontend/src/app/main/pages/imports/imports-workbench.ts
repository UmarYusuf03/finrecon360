import { CommonModule } from '@angular/common';
import { HttpErrorResponse } from '@angular/common/http';
import { Component, OnInit } from '@angular/core';
import { CdkDragDrop, DragDropModule } from '@angular/cdk/drag-drop';
import { FormsModule } from '@angular/forms';
import { Router } from '@angular/router';

import { AuthService } from '../../../core/auth/auth.service';
import { AdminImportArchitectureService } from '../../../core/admin-rbac/admin-import-architecture.service';
import { ImportMappingTemplate } from '../../../core/admin-rbac/models';
import { ImportsService } from '../../../core/imports/imports.service';
import {
  ImportActiveTemplateResponse,
  ImportHistoryItem,
  ImportParseResponse,
  ImportValidationRow,
  ImportValidationRowsResponse,
  ImportValidateResponse,
} from '../../../core/imports/imports.models';

type ValidationSummary = {
  missingRequired: number;
  invalidDate: number;
  invalidAmount: number;
  conflictingAmounts: number;
  other: number;
};

@Component({
  selector: 'app-imports-workbench',
  standalone: true,
  imports: [CommonModule, FormsModule, DragDropModule],
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
  selectedFileName: string | null = null;
  selectedFileSize: number | null = null;
  isDragging = false;

  history: ImportHistoryItem[] = [];
  selectedBatch: ImportHistoryItem | null = null;
  search = '';
  statusFilter = '';

  parseResult: ImportParseResponse | null = null;
  validateResult: ImportValidateResponse | null = null;
  validationSummary: ValidationSummary | null = null;
  validationRows: ImportValidationRow[] = [];
  validationTotals: ImportValidationRowsResponse | null = null;
  validationFilter = 'INVALID';

  mapping: Record<string, string> = {};
  activeTemplate: ImportActiveTemplateResponse | null = null;
  templates: ImportMappingTemplate[] = [];
  selectedTemplateId: string | null = null;
  templateLoading = false;

  canManage = false;
  private authRetryInProgress = false;
  deleteDialogOpen = false;
  deleteTarget: ImportHistoryItem | null = null;

  constructor(
    private readonly importsService: ImportsService,
    private readonly importArchitectureService: AdminImportArchitectureService,
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
    const file = input.files?.item(0) ?? null;
    this.setSelectedFile(file);
  }

  onDragOver(event: DragEvent): void {
    event.preventDefault();
    this.isDragging = true;
  }

  onDragLeave(event: DragEvent): void {
    event.preventDefault();
    this.isDragging = false;
  }

  onDrop(event: DragEvent): void {
    event.preventDefault();
    this.isDragging = false;
    const file = event.dataTransfer?.files?.item(0) ?? null;
    this.setSelectedFile(file);
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
        this.setSelectedFile(null);
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
    this.validationSummary = null;
    this.validationRows = [];
    this.validationTotals = null;
    this.mapping = {};
    this.activeTemplate = null;
    this.templates = [];
    this.selectedTemplateId = null;
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
        this.loadActiveTemplate();
        this.loadTemplatesForSource();
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
        this.validationSummary = this.buildValidationSummary(res.errors);
        this.loadValidationRows();
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
          this.validationRows = [];
          this.validationTotals = null;
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

  useActiveTemplate(): void {
    this.applyActiveTemplate(true);
  }

  applySelectedTemplate(): void {
    if (!this.selectedTemplateId) {
      return;
    }

    const template = this.templates.find((item) => item.id === this.selectedTemplateId);
    if (!template) {
      return;
    }

    const templateMappings = this.normalizeTemplateMappings(template.mappingJson);
    if (!templateMappings) {
      this.actionError = 'Selected mapping template has invalid JSON.';
      return;
    }

    this.canonicalFields.forEach((field) => {
      this.mapping[field] = templateMappings[field] ?? '';
    });

    this.actionMessage = `Applied mapping template "${template.name}".`;
  }

  assignFieldToHeader(field: string, header: string | null): void {
    if (!header) {
      this.mapping[field] = '';
      return;
    }

    const existingField = this.getAssignedFieldForHeader(header);
    if (existingField && existingField !== field) {
      this.mapping[existingField] = '';
    }

    this.mapping[field] = header;
  }

  onFieldDropped(event: CdkDragDrop<string[]>, header: string): void {
    const field = event.item.data as string;
    this.assignFieldToHeader(field, header);
  }

  getAssignedFieldForHeader(header: string): string | null {
    return this.canonicalFields.find((field) => this.mapping[field] === header) ?? null;
  }

  getUnassignedFields(): string[] {
    return this.canonicalFields.filter((field) => !this.mapping[field]);
  }

  clearHeaderMapping(header: string): void {
    const assigned = this.getAssignedFieldForHeader(header);
    if (assigned) {
      this.mapping[assigned] = '';
    }
  }

  loadValidationRows(): void {
    if (!this.selectedBatch) return;

    this.importsService.getValidationRows(this.selectedBatch.id, this.validationFilter).subscribe({
      next: (res) => {
        this.validationTotals = res;
        this.validationRows = res.rows;
      },
      error: (error) => {
        if (this.tryRefreshSessionAndRetry(error, () => this.loadValidationRows())) {
          return;
        }
        this.actionError = this.getErrorMessage(error, 'Unable to load validation rows.');
      },
    });
  }

  updateRow(row: ImportValidationRow): void {
    if (!this.selectedBatch) return;

    this.processing = true;
    this.clearAlerts();
    this.importsService
      .updateRawRecord(this.selectedBatch.id, row.rawRecordId, { payload: row.payload })
      .subscribe({
        next: (updated) => {
          this.processing = false;
          const index = this.validationRows.findIndex((r) => r.rawRecordId === updated.rawRecordId);
          if (index >= 0) {
            this.validationRows[index] = updated;
          }
          this.loadValidationRows();
          this.actionMessage = 'Row updated.';
        },
        error: (error) => {
          this.processing = false;
          if (this.tryRefreshSessionAndRetry(error, () => this.updateRow(row))) {
            return;
          }
          this.actionError = this.getErrorMessage(error, 'Unable to update row.');
        },
      });
  }

  private loadActiveTemplate(): void {
    if (!this.selectedBatch || !this.canManage) {
      return;
    }

    this.importsService.getActiveTemplate(this.selectedBatch.sourceType).subscribe({
      next: (template) => {
        this.activeTemplate = template;
        if (!this.selectedTemplateId) {
          this.selectedTemplateId = template.id;
        }
        const applied = this.applyActiveTemplate(false);
        if (applied) {
          this.actionMessage = `File parsed. Applied mapping template "${template.name}".`;
        }
      },
      error: (error) => {
        if (this.tryRefreshSessionAndRetry(error, () => this.loadActiveTemplate())) {
          return;
        }

        if (error instanceof HttpErrorResponse && error.status === 404) {
          this.activeTemplate = null;
          return;
        }

        this.actionError = this.getErrorMessage(error, 'Unable to load active template.');
      },
    });
  }

  private loadTemplatesForSource(): void {
    if (!this.selectedBatch || !this.canManage) {
      return;
    }

    this.templateLoading = true;
    this.importArchitectureService.getMappingTemplates(this.selectedBatch.sourceType).subscribe({
      next: (templates) => {
        this.templateLoading = false;
        this.templates = templates;
        if (!this.selectedTemplateId && templates.length > 0) {
          this.selectedTemplateId = templates[0].id;
        }
      },
      error: (error: unknown) => {
        this.templateLoading = false;
        this.actionError = this.getErrorMessage(error, 'Unable to load mapping templates.');
      },
    });
  }

  private applyActiveTemplate(force: boolean): boolean {
    if (!this.activeTemplate || !this.parseResult) {
      return false;
    }

    const shouldApply = force || this.canonicalFields.every((field) => !this.mapping[field]);
    if (!shouldApply) {
      return false;
    }

    const templateMappings = this.normalizeTemplateMappings(this.activeTemplate.mappingJson);
    if (!templateMappings) {
      this.actionError = 'Active mapping template has invalid JSON.';
      return false;
    }

    this.canonicalFields.forEach((field) => {
      this.mapping[field] = templateMappings[field] ?? '';
    });

    return true;
  }

  private normalizeTemplateMappings(payload: string): Record<string, string> | null {
    try {
      const parsed = JSON.parse(payload) as Record<string, string>;
      if (!parsed || typeof parsed !== 'object') {
        return null;
      }
      const canonicalSet = new Set(this.canonicalFields);
      const keys = Object.keys(parsed);

      if (keys.some((key) => canonicalSet.has(key))) {
        const normalized: Record<string, string> = {};
        this.canonicalFields.forEach((field) => {
          if (parsed[field]) {
            normalized[field] = parsed[field];
          }
        });
        return Object.keys(normalized).length > 0 ? normalized : null;
      }

      const inverted: Record<string, string> = {};
      keys.forEach((header) => {
        const field = parsed[header];
        if (field && canonicalSet.has(field)) {
          inverted[field] = header;
        }
      });

      return Object.keys(inverted).length > 0 ? inverted : null;
    } catch {
      return null;
    }
  }

  private setSelectedFile(file: File | null): void {
    this.selectedFile = file;
    this.selectedFileName = file?.name ?? null;
    this.selectedFileSize = file?.size ?? null;
  }

  private clearAlerts(): void {
    this.actionMessage = null;
    this.actionError = null;
  }

  private buildValidationSummary(errors: { message: string }[]): ValidationSummary | null {
    if (!errors || errors.length === 0) {
      return null;
    }

    const summary: ValidationSummary = {
      missingRequired: 0,
      invalidDate: 0,
      invalidAmount: 0,
      conflictingAmounts: 0,
      other: 0,
    };

    errors.forEach((error) => {
      const message = error.message.toLowerCase();
      if (message.includes('required')) {
        summary.missingRequired += 1;
        return;
      }

      if (message.includes('date') && message.includes('invalid')) {
        summary.invalidDate += 1;
        return;
      }

      if (message.includes('amount') || message.includes('number')) {
        summary.invalidAmount += 1;
        return;
      }

      if (message.includes('both debit and credit') || message.includes('net amount')) {
        summary.conflictingAmounts += 1;
        return;
      }

      summary.other += 1;
    });

    return summary;
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
