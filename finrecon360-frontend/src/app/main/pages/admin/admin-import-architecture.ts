import { Component, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { HttpErrorResponse } from '@angular/common/http';
import { FormBuilder, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatCheckboxModule } from '@angular/material/checkbox';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatSnackBar, MatSnackBarModule } from '@angular/material/snack-bar';
import { MatTableModule } from '@angular/material/table';
import { TranslateModule } from '@ngx-translate/core';

import { AdminImportArchitectureService } from '../../../core/admin-rbac/admin-import-architecture.service';
import {
  CanonicalField,
  ImportArchitectureOverview,
  ImportMappingTemplate,
} from '../../../core/admin-rbac/models';

@Component({
  selector: 'app-admin-import-architecture',
  standalone: true,
  imports: [
    CommonModule,
    ReactiveFormsModule,
    MatCardModule,
    MatTableModule,
    MatButtonModule,
    MatFormFieldModule,
    MatInputModule,
    MatCheckboxModule,
    MatSnackBarModule,
    TranslateModule,
  ],
  templateUrl: './admin-import-architecture.html',
  styleUrls: ['./admin-import-architecture.scss'],
})
export class AdminImportArchitectureComponent implements OnInit {
  overview: ImportArchitectureOverview | null = null;
  templates: ImportMappingTemplate[] = [];
  displayedCanonicalColumns = ['field', 'dataType', 'required', 'description'];
  displayedTemplateColumns = ['name', 'sourceType', 'version', 'status', 'updatedAt', 'actions'];
  loading = false;
  saving = false;
  editingTemplateId: string | null = null;
  deleteDialogOpen = false;
  deleteTarget: ImportMappingTemplate | null = null;

  templateForm!: FormGroup;

  constructor(
    private readonly fb: FormBuilder,
    private readonly importArchitectureService: AdminImportArchitectureService,
    private readonly snackBar: MatSnackBar,
  ) {}

  ngOnInit(): void {
    this.templateForm = this.fb.group({
      name: ['', [Validators.required, Validators.maxLength(150)]],
      sourceType: ['', [Validators.required, Validators.maxLength(100)]],
      canonicalSchemaVersion: ['v1', [Validators.required, Validators.maxLength(30)]],
      mappingJson: ['', [Validators.required]],
      isActive: [true],
    });

    this.loadData();
  }

  get canonicalFields(): CanonicalField[] {
    return this.overview?.canonicalSchema.fields ?? [];
  }

  trackByTemplateId(_: number, template: ImportMappingTemplate): string {
    return template.id;
  }

  startCreate(): void {
    this.editingTemplateId = null;
    this.templateForm.reset({
      name: '',
      sourceType: '',
      canonicalSchemaVersion: this.overview?.canonicalSchema.version ?? 'v1',
      mappingJson: '',
      isActive: true,
    });
  }

  startEdit(template: ImportMappingTemplate): void {
    this.editingTemplateId = template.id;
    this.templateForm.reset({
      name: template.name,
      sourceType: template.sourceType,
      canonicalSchemaVersion: template.canonicalSchemaVersion,
      mappingJson: template.mappingJson,
      isActive: template.isActive,
    });
  }

  startRename(template: ImportMappingTemplate): void {
    this.startEdit(template);
  }

  reuseTemplate(template: ImportMappingTemplate): void {
    this.editingTemplateId = null;
    this.templateForm.reset({
      name: `${template.name} Copy`,
      sourceType: template.sourceType,
      canonicalSchemaVersion: template.canonicalSchemaVersion,
      mappingJson: template.mappingJson,
      isActive: true,
    });
  }

  saveTemplate(): void {
    if (this.templateForm.invalid) {
      this.templateForm.markAllAsTouched();
      return;
    }

    const formValue = this.templateForm.getRawValue() as {
      name: string;
      sourceType: string;
      canonicalSchemaVersion: string;
      mappingJson: string;
      isActive: boolean;
    };

    if (!this.looksLikeJson(formValue.mappingJson)) {
      this.snackBar.open('Mapping JSON must be valid JSON.', 'Close', { duration: 3500 });
      return;
    }

    this.saving = true;

    if (!this.editingTemplateId) {
      this.importArchitectureService
        .createMappingTemplate({
          name: formValue.name,
          sourceType: formValue.sourceType,
          canonicalSchemaVersion: formValue.canonicalSchemaVersion,
          mappingJson: formValue.mappingJson,
        })
        .subscribe({
          next: () => {
            this.saving = false;
            this.snackBar.open('Mapping template created.', 'Close', { duration: 2500 });
            this.startCreate();
            this.loadTemplates();
          },
          error: (error: unknown) => {
            this.saving = false;
            this.snackBar.open(this.extractErrorMessage(error), 'Close', { duration: 3500 });
          },
        });
      return;
    }

    this.importArchitectureService
      .updateMappingTemplate(this.editingTemplateId, {
        name: formValue.name,
        sourceType: formValue.sourceType,
        canonicalSchemaVersion: formValue.canonicalSchemaVersion,
        mappingJson: formValue.mappingJson,
        isActive: formValue.isActive,
      })
      .subscribe({
        next: () => {
          this.saving = false;
          this.snackBar.open('Mapping template updated.', 'Close', { duration: 2500 });
          this.startCreate();
          this.loadTemplates();
        },
        error: (error: unknown) => {
          this.saving = false;
          this.snackBar.open(this.extractErrorMessage(error), 'Close', { duration: 3500 });
        },
      });
  }

  deactivateTemplate(template: ImportMappingTemplate): void {
    this.importArchitectureService.deactivateMappingTemplate(template.id).subscribe({
      next: () => {
        this.snackBar.open('Template deactivated.', 'Close', { duration: 2500 });
        this.loadTemplates();
      },
      error: (error: unknown) => {
        this.snackBar.open(this.extractErrorMessage(error), 'Close', { duration: 3500 });
      },
    });
  }

  deleteTemplate(template: ImportMappingTemplate): void {
    this.deleteTarget = template;
    this.deleteDialogOpen = true;
  }

  confirmDeleteTemplate(): void {
    if (!this.deleteTarget) {
      this.deleteDialogOpen = false;
      return;
    }

    const target = this.deleteTarget;
    this.deleteDialogOpen = false;
    this.deleteTarget = null;

    this.importArchitectureService.deleteMappingTemplate(target.id).subscribe({
      next: () => {
        this.snackBar.open('Template deleted.', 'Close', { duration: 2500 });
        if (this.editingTemplateId === target.id) {
          this.startCreate();
        }
        this.loadTemplates();
      },
      error: (error: unknown) => {
        this.snackBar.open(this.extractErrorMessage(error), 'Close', { duration: 3500 });
      },
    });
  }

  closeDeleteDialog(): void {
    this.deleteDialogOpen = false;
    this.deleteTarget = null;
  }

  private loadData(): void {
    this.loading = true;
    this.importArchitectureService.getOverview().subscribe({
      next: (overview) => {
        this.overview = overview;
        this.loading = false;
        this.startCreate();
      },
      error: (error: unknown) => {
        this.loading = false;
        this.snackBar.open(this.extractErrorMessage(error), 'Close', { duration: 3500 });
      },
    });

    this.loadTemplates();
  }

  private loadTemplates(): void {
    this.importArchitectureService.getMappingTemplates().subscribe({
      next: (templates) => {
        this.templates = templates;
      },
      error: (error: unknown) => {
        this.snackBar.open(this.extractErrorMessage(error), 'Close', { duration: 3500 });
      },
    });
  }

  private looksLikeJson(value: string): boolean {
    try {
      JSON.parse(value);
      return true;
    } catch {
      return false;
    }
  }

  private extractErrorMessage(error: unknown): string {
    if (error instanceof HttpErrorResponse) {
      const body = error.error as { message?: string } | null;
      return body?.message ?? `Request failed with status ${error.status}.`;
    }

    return 'Request failed.';
  }
}
