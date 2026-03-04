import { Component, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormBuilder, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatDialog, MatDialogModule } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatSlideToggleModule } from '@angular/material/slide-toggle';
import { MatTableModule } from '@angular/material/table';
import { TranslateModule } from '@ngx-translate/core';

import { HasPermissionDirective } from '../../../core/auth/has-permission.directive';
import { AdminRoleService } from '../../../core/admin-rbac/admin-role.service';
import { Role } from '../../../core/admin-rbac/models';

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

  constructor(
    private adminRoleService: AdminRoleService,
    private fb: FormBuilder,
    private dialog: MatDialog
  ) {}

  ngOnInit(): void {
    this.form = this.fb.group({
      code: ['', Validators.required],
      name: ['', Validators.required],
      description: [''],
    });
    this.adminRoleService.getRoles().subscribe((roles) => (this.roles = roles));
  }

  openAdd(dialogTemplate: any): void {
    this.editingId = null;
    this.form.reset();
    this.dialog.open(dialogTemplate);
  }

  openEdit(role: Role, dialogTemplate: any): void {
    this.editingId = role.id;
    this.form.patchValue({
      code: role.code,
      name: role.name,
      description: role.description,
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
      this.adminRoleService.updateRole(this.editingId, payload).subscribe();
    } else {
      this.adminRoleService.createRole(payload).subscribe();
    }
    this.dialog.closeAll();
  }

  deactivate(role: Role): void {
    if (role.isSystem) return; // avoid switching off built-ins
    this.adminRoleService.deactivateRole(role.id).subscribe();
  }

  reactivate(role: Role): void {
    this.adminRoleService.reactivateRole(role.id).subscribe();
  }
}
