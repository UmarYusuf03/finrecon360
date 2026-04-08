import { Component, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { HttpErrorResponse } from '@angular/common/http';
import { FormBuilder, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatChipsModule } from '@angular/material/chips';
import { MatDialog, MatDialogModule } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatSnackBar, MatSnackBarModule } from '@angular/material/snack-bar';
import { MatSelectModule } from '@angular/material/select';
import { MatTableModule } from '@angular/material/table';
import { TranslateModule } from '@ngx-translate/core';
import { Observable } from 'rxjs';
import { switchMap } from 'rxjs/operators';

import { AdminRoleService } from '../../../core/admin-rbac/admin-role.service';
import { AdminUserService } from '../../../core/admin-rbac/admin-user.service';
import { AdminUserSummary, Role } from '../../../core/admin-rbac/models';
import { HasPermissionDirective } from '../../../core/auth/has-permission.directive';

@Component({
  selector: 'app-admin-users',
  standalone: true,
  imports: [
    CommonModule,
    ReactiveFormsModule,
    MatCardModule,
    MatTableModule,
    MatButtonModule,
    MatDialogModule,
    MatFormFieldModule,
    MatInputModule,
    MatSnackBarModule,
    MatSelectModule,
    MatIconModule,
    MatChipsModule,
    TranslateModule,
    HasPermissionDirective,
  ],
  templateUrl: './admin-users.html',
  styleUrls: ['./admin-users.scss'],
})
export class AdminUsersComponent implements OnInit {
  private static readonly AdminRoleCode = 'ADMIN';
  displayedColumns = ['name', 'email', 'roles', 'status', 'actions'];
  users: AdminUserSummary[] = [];
  roles: Role[] = [];
  form!: FormGroup;
  editingId: string | null = null;
  saveError: string | null = null;

  constructor(
    private adminUserService: AdminUserService,
    private adminRoleService: AdminRoleService,
    private fb: FormBuilder,
    private dialog: MatDialog,
    private snackBar: MatSnackBar
  ) {}

  ngOnInit(): void {
    this.form = this.fb.group({
      displayName: ['', Validators.required],
      email: ['', [Validators.required, Validators.email]],
      password: [''],
      roles: [[], Validators.required],
    });

    this.adminRoleService.getRoles().subscribe((roles) => (this.roles = roles));
    this.adminUserService.getUsers().subscribe((users) => (this.users = users));
  }

  openAdd(dialogTemplate: any): void {
    this.editingId = null;
    this.saveError = null;
    this.form.reset({ roles: [] });
    this.dialog.open(dialogTemplate);
  }

  openEdit(user: AdminUserSummary, dialogTemplate: any): void {
    this.editingId = user.id;
    this.saveError = null;
    this.form.patchValue({
      displayName: user.displayName,
      email: user.email,
      roles: user.roles,
      password: '',
    });
    this.dialog.open(dialogTemplate);
  }

  save(): void {
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }
    this.saveError = null;
    const payload = this.form.value as {
      displayName: string;
      email: string;
      password?: string;
      roles: string[];
    };
    const selectedRoleCodes = (payload.roles ?? []).map((role) => role.toUpperCase());

    let saveRequest$: Observable<unknown>;
    if (this.editingId) {
      const editingUser = this.users.find((user) => user.id === this.editingId);
      const wasAdmin = editingUser?.roles.some((role) => role.toUpperCase() === AdminUsersComponent.AdminRoleCode) ?? false;
      const willBeAdmin = selectedRoleCodes.includes(AdminUsersComponent.AdminRoleCode);
      if (wasAdmin && !willBeAdmin) {
        const hasAnotherAdmin = this.users.some(
          (user) =>
            user.id !== this.editingId &&
            user.roles.some((role) => role.toUpperCase() === AdminUsersComponent.AdminRoleCode)
        );
        if (!hasAnotherAdmin) {
          this.saveError = 'Cannot remove ADMIN from the last tenant admin. Assign ADMIN to another user first.';
          return;
        }
      }

      saveRequest$ = this.adminUserService
        .updateUser(this.editingId, payload)
        .pipe(switchMap(() => this.adminUserService.setUserRoles(this.editingId!, payload.roles)));
    } else {
      saveRequest$ = this.adminUserService.createUser(payload);
    }

    saveRequest$.subscribe({
      next: () => {
        this.dialog.closeAll();
        this.snackBar.open(this.editingId ? 'User updated successfully.' : 'User created successfully.', 'Close', { duration: 2500 });
      },
      error: (error: unknown) => {
        const message = this.extractErrorMessage(error);
        this.saveError = message;
        this.snackBar.open(message, 'Close', { duration: 3500 });
      },
    });
  }

  toggleActive(user: AdminUserSummary): void {
    if (user.isActive) {
      this.adminUserService.deactivateUser(user.id).subscribe({
        next: () => this.snackBar.open('User deactivated.', 'Close', { duration: 2500 }),
        error: (error: unknown) => this.snackBar.open(this.extractErrorMessage(error), 'Close', { duration: 3500 }),
      });
    } else {
      this.adminUserService.reactivateUser(user.id).subscribe({
        next: () => this.snackBar.open('User reactivated.', 'Close', { duration: 2500 }),
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
