import { Component, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { HttpErrorResponse } from '@angular/common/http';
import { FormBuilder, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatDialog, MatDialogModule } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatSnackBar, MatSnackBarModule } from '@angular/material/snack-bar';
import { MatTableModule } from '@angular/material/table';
import { TranslateModule } from '@ngx-translate/core';

import { AdminComponentService } from '../../../core/admin-rbac/admin-component.service';
import { AppComponentResource } from '../../../core/admin-rbac/models';
import { HasPermissionDirective } from '../../../core/auth/has-permission.directive';

/**
 * WHY: This component serves as the CRUD interface for tracking logical 'Components' 
 * or 'Feature Modules' within the tenant. Form state is managed locally and API actions 
 * are delegated to `AdminComponentService` to keep UI components strictly presentational.
 */
@Component({
  selector: 'app-admin-components',
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
    TranslateModule,
    HasPermissionDirective,
  ],
  templateUrl: './admin-components.html',
  styleUrls: ['./admin-components.scss'],
})
export class AdminComponentsComponent implements OnInit {
  displayedColumns = ['code', 'name', 'route', 'category', 'status', 'actions'];
  components: AppComponentResource[] = [];
  form!: FormGroup;
  editingId: string | null = null;

  constructor(
    private adminComponentService: AdminComponentService,
    private fb: FormBuilder,
    private dialog: MatDialog,
    private snackBar: MatSnackBar
  ) {}

  ngOnInit(): void {
    this.form = this.fb.group({
      code: ['', Validators.required],
      name: ['', Validators.required],
      routePath: ['', Validators.required],
      category: [''],
      description: [''],
    });

    this.adminComponentService.getComponents().subscribe((components) => (this.components = components));
  }

  openAdd(dialogTemplate: any): void {
    this.editingId = null;
    this.form.reset();
    this.dialog.open(dialogTemplate);
  }

  openEdit(component: AppComponentResource, dialogTemplate: any): void {
    this.editingId = component.id;
    this.form.patchValue({
      code: component.code,
      name: component.name,
      routePath: component.routePath,
      category: component.category,
      description: component.description,
    });
    this.dialog.open(dialogTemplate);
  }

  save(): void {
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }
    const payload = this.form.value;
    if (this.editingId) {
      this.adminComponentService.updateComponent(this.editingId, payload).subscribe({
        next: () => {
          this.dialog.closeAll();
          this.snackBar.open('Component updated successfully.', 'Close', { duration: 2500 });
        },
        error: (error: unknown) => {
          this.snackBar.open(this.extractErrorMessage(error), 'Close', { duration: 3500 });
        },
      });
    } else {
      this.adminComponentService.createComponent(payload).subscribe({
        next: () => {
          this.dialog.closeAll();
          this.snackBar.open('Component created successfully.', 'Close', { duration: 2500 });
        },
        error: (error: unknown) => {
          this.snackBar.open(this.extractErrorMessage(error), 'Close', { duration: 3500 });
        },
      });
    }
  }

  toggleActive(component: AppComponentResource): void {
    if (component.isActive) {
      this.adminComponentService.deactivateComponent(component.id).subscribe({
        next: () => this.snackBar.open('Component deactivated.', 'Close', { duration: 2500 }),
        error: (error: unknown) => this.snackBar.open(this.extractErrorMessage(error), 'Close', { duration: 3500 }),
      });
    } else {
      this.adminComponentService.reactivateComponent(component.id).subscribe({
        next: () => this.snackBar.open('Component reactivated.', 'Close', { duration: 2500 }),
        error: (error: unknown) => this.snackBar.open(this.extractErrorMessage(error), 'Close', { duration: 3500 }),
      });
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
