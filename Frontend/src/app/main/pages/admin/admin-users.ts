import { Component, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormBuilder, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatChipsModule } from '@angular/material/chips';
import { MatDialog, MatDialogModule } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatSelectModule } from '@angular/material/select';
import { MatTableModule } from '@angular/material/table';
import { TranslateModule } from '@ngx-translate/core';

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
  displayedColumns = ['name', 'email', 'roles', 'status', 'actions'];
  users: AdminUserSummary[] = [];
  roles: Role[] = [];
  form!: FormGroup;
  editingId: string | null = null;

  constructor(
    private adminUserService: AdminUserService,
    private adminRoleService: AdminRoleService,
    private fb: FormBuilder,
    private dialog: MatDialog
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
    this.form.reset({ roles: [] });
    this.dialog.open(dialogTemplate);
  }

  openEdit(user: AdminUserSummary, dialogTemplate: any): void {
    this.editingId = user.id;
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
    const payload = this.form.value;
    if (this.editingId) {
      this.adminUserService.updateUser(this.editingId, payload).subscribe();
      this.adminUserService.setUserRoles(this.editingId, payload.roles).subscribe();
    } else {
      this.adminUserService.createUser(payload).subscribe();
    }
    this.dialog.closeAll();
  }

  toggleActive(user: AdminUserSummary): void {
    if (user.isActive) {
      this.adminUserService.deactivateUser(user.id).subscribe();
    } else {
      this.adminUserService.reactivateUser(user.id).subscribe();
    }
  }
}
