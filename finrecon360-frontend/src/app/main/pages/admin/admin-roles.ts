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
import { MatSlideToggleModule } from '@angular/material/slide-toggle';
import { MatTableModule } from '@angular/material/table';
import { TranslateModule } from '@ngx-translate/core';

import { HasPermissionDirective } from '../../../core/auth/has-permission.directive';
import { AdminRoleService } from '../../../core/admin-rbac/admin-role.service';
import { Role } from '../../../core/admin-rbac/models';

/**
 * WHY: This component serves as the CRUD interface for Roles. 
 * Form state is managed locally here rather than via NgRx/Redux to minimize boilerplate 
 * for simple entity administration, delegating standard caching to the `AdminRoleService`.
 */
@Component({
  selector: 'app-admin-roles',
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
    MatSlideToggleModule,
    TranslateModule,
    HasPermissionDirective,
  ],
  templateUrl: './admin-roles.html',
})
export class AdminRolesComponent implements OnInit {
  displayedColumns = ['code', 'name', 'description', 'status', 'system', 'actions'];
  roles: Role[] = [];
  form!: FormGroup;
  editingId: string | null = null;
  private editingRoleIsSystem = false;

  constructor(
    private adminRoleService: AdminRoleService,
    private fb: FormBuilder,
    private dialog: MatDialog,
    private snackBar: MatSnackBar
  ) {}

  ngOnInit(): void {
    this.form = this.fb.group({
      code: ['', Validators.required],
      name: ['', Validators.required],
      description: [''],
    });

    /**
     * WHY: We subscribe to the `BehaviorSubject` of the service.
     * The list is sorted to always pin 'System' (immutable/core) roles at the top,
     * so that administrators immediately see the most critical built-in personas first.
     */
    this.adminRoleService.getRoles().subscribe((roles) => {
      this.roles = [...roles].sort((left, right) => {
        if (!!left.isSystem !== !!right.isSystem) {
          return left.isSystem ? -1 : 1;
        }
        return left.name.localeCompare(right.name);
      });
    });
  }

  openAdd(dialogTemplate: any): void {
    this.editingId = null;
    this.editingRoleIsSystem = false;
    this.form.reset();
    this.form.get('code')?.enable();
    this.dialog.open(dialogTemplate);
  }

  openEdit(role: Role, dialogTemplate: any): void {
    this.editingId = role.id;
    this.editingRoleIsSystem = !!role.isSystem;
    this.form.patchValue({
      code: role.code,
      name: role.name,
      description: role.description,
    });
    if (this.editingRoleIsSystem) {
      this.form.get('code')?.disable();
    } else {
      this.form.get('code')?.enable();
    }
    this.dialog.open(dialogTemplate);
  }

  save(): void {
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }
    const payload = this.form.getRawValue();
    if (this.editingId) {
      this.adminRoleService.updateRole(this.editingId, payload).subscribe({
        next: () => {
          this.closeDialogIfOpen();
          this.snackBar.open('Role updated successfully.', 'Close', { duration: 2500 });
        },
        error: (error: unknown) => {
          this.snackBar.open(this.extractErrorMessage(error), 'Close', { duration: 3500 });
        },
      });
    } else {
      this.adminRoleService.createRole(payload).subscribe({
        next: () => {
          this.closeDialogIfOpen();
          this.snackBar.open('Role created successfully.', 'Close', { duration: 2500 });
        },
        error: (error: unknown) => {
          this.snackBar.open(this.extractErrorMessage(error), 'Close', { duration: 3500 });
        },
      });
    }
  }

  /**
   * WHY: System roles are intrinsic to the operational logic of the platform 
   * (e.g., hardcoded checks for Super Admin). Deactivating them would break core workflows.
   */
  deactivate(role: Role): void {
    if (role.isSystem) return; // avoid switching off built-ins
    this.adminRoleService.deactivateRole(role.id).subscribe({
      next: () => this.snackBar.open('Role deactivated.', 'Close', { duration: 2500 }),
      error: (error: unknown) => this.snackBar.open(this.extractErrorMessage(error), 'Close', { duration: 3500 }),
    });
  }

  reactivate(role: Role): void {
    this.adminRoleService.reactivateRole(role.id).subscribe({
      next: () => this.snackBar.open('Role reactivated.', 'Close', { duration: 2500 }),
      error: (error: unknown) => this.snackBar.open(this.extractErrorMessage(error), 'Close', { duration: 3500 }),
    });
  }

  private closeDialogIfOpen(): void {
    if (!Array.isArray(this.dialog.openDialogs) || this.dialog.openDialogs.length === 0) {
      return;
    }
    this.dialog.closeAll();
  }

  private extractErrorMessage(error: unknown): string {
    if (error instanceof HttpErrorResponse) {
      const body = error.error as { message?: string } | null;
      return body?.message ?? `Request failed with status ${error.status}.`;
    }
    return 'Request failed.';
  }
}
