import { Component, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormBuilder, FormGroup, ReactiveFormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatCheckboxModule } from '@angular/material/checkbox';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatSnackBar, MatSnackBarModule } from '@angular/material/snack-bar';
import { MatSelectModule } from '@angular/material/select';
import { MatTableModule } from '@angular/material/table';
import { TranslateModule, TranslateService } from '@ngx-translate/core';
import { combineLatest } from 'rxjs';
import { distinctUntilChanged } from 'rxjs/operators';

import { AdminPermissionService } from '../../../core/admin-rbac/admin-permission.service';
import { AdminComponentService } from '../../../core/admin-rbac/admin-component.service';
import { AdminRoleService } from '../../../core/admin-rbac/admin-role.service';
import { HasPermissionDirective } from '../../../core/auth/has-permission.directive';
import {
  ActionDefinition,
  AppComponentResource,
  PermissionAssignment,
  Role,
} from '../../../core/admin-rbac/models';

@Component({
  selector: 'app-admin-permissions',
  standalone: true,
  imports: [
    CommonModule,
    ReactiveFormsModule,
    MatCardModule,
    MatTableModule,
    MatCheckboxModule,
    MatButtonModule,
    MatFormFieldModule,
    MatInputModule,
    MatSnackBarModule,
    MatSelectModule,
    TranslateModule,
    HasPermissionDirective,
  ],
  templateUrl: './admin-permissions.html',
  styleUrls: ['./admin-permissions.scss'],
})
export class AdminPermissionsComponent implements OnInit {
  private static readonly ManageImpliedActions = ['VIEW', 'VIEW_LIST', 'CREATE', 'EDIT', 'DELETE'];
  private static readonly ManageRequiredActions = ['CREATE', 'EDIT', 'DELETE'];
  private static readonly HiddenActionCodes = new Set(['VIEW_LIST']);

  roles: Role[] = [];
  components: AppComponentResource[] = [];
  actions: ActionDefinition[] = [];
  visibleActions: ActionDefinition[] = [];
  assignments: PermissionAssignment[] = [];
  originalAssignments: PermissionAssignment[] = [];
  availablePermissionCodes = new Set<string>();
  displayedColumns: string[] = ['component'];
  saving = false;

  form!: FormGroup;

  constructor(
    private fb: FormBuilder,
    private permissionService: AdminPermissionService,
    private roleService: AdminRoleService,
    private componentService: AdminComponentService,
    private translate: TranslateService,
    private snackBar: MatSnackBar
  ) {}

  ngOnInit(): void {
    this.form = this.fb.group({
      roleId: [''],
      search: [''],
    });

    combineLatest({
      roles: this.roleService.getRoles(),
      components: this.componentService.getComponents(),
      actions: this.permissionService.getActions(),
      availableCodes: this.permissionService.getAvailablePermissionCodes(),
    }).subscribe(({ roles, components, actions, availableCodes }) => {
      this.roles = roles;
      this.components = components;
      this.actions = actions;
      this.visibleActions = actions.filter((action) => !AdminPermissionsComponent.HiddenActionCodes.has(action.code));
      this.availablePermissionCodes = availableCodes;
      this.displayedColumns = ['component', ...this.visibleActions.map((a) => a.code)];

      const currentRoleId = this.form.get('roleId')?.value;
      const currentRoleStillExists = roles.some((role) => role.id === currentRoleId);

      if (!currentRoleStillExists && roles.length) {
        this.form.get('roleId')?.setValue(roles[0].id);
      }
    });

    this.form.get('roleId')?.valueChanges.pipe(distinctUntilChanged()).subscribe((roleId) => {
      if (roleId) {
        this.loadAssignments(roleId);
      }
    });
  }

  filteredComponents(): AppComponentResource[] {
    const search = (this.form.get('search')?.value || '').toLowerCase();
    if (!search) return this.components;
    return this.components.filter((c) => c.name.toLowerCase().includes(search) || c.code.toLowerCase().includes(search));
  }

  isChecked(componentId: string, actionCode: string): boolean {
    const roleId = this.form.get('roleId')?.value;
    const component = this.components.find((item) => item.id === componentId);
    if (!component) {
      return false;
    }
    const permissionCode = this.permissionService.getPermissionCodeForComponent(component.code, actionCode);
    return this.assignments.some(
      (a) => a.roleId === roleId && a.permissionCode === permissionCode
    );
  }

  isApplicable(component: AppComponentResource, action: ActionDefinition): boolean {
    if (this.availablePermissionCodes.size === 0) {
      return true;
    }

    const code = this.permissionService.getPermissionCodeForComponent(component.code, action.code);
    return this.availablePermissionCodes.has(code);
  }

  toggle(component: AppComponentResource, action: ActionDefinition): void {
    const roleId = this.form.get('roleId')?.value;
    if (!roleId || !this.isApplicable(component, action)) return;

    const currentlyChecked = this.isChecked(component.id, action.code);

    if (action.code === 'MANAGE') {
      this.setAssignment(roleId, component, action.code, !currentlyChecked);
      if (!currentlyChecked) {
        AdminPermissionsComponent.ManageImpliedActions.forEach((impliedActionCode) => {
          const impliedAction = this.actions.find((candidate) => candidate.code === impliedActionCode);
          if (impliedAction && this.isApplicable(component, impliedAction)) {
            this.setAssignment(roleId, component, impliedActionCode, true);
          }
        });
      }
      return;
    }

    if (action.code === 'VIEW') {
      this.setAssignment(roleId, component, action.code, !currentlyChecked);
      if (currentlyChecked) {
        this.actions
          .filter((candidate) => candidate.code !== 'VIEW')
          .forEach((candidate) => this.setAssignment(roleId, component, candidate.code, false));
      }
      this.enforceManageDependencies(roleId, component);
      return;
    }

    this.setAssignment(roleId, component, action.code, !currentlyChecked);
    if (!currentlyChecked) {
      const viewAction = this.actions.find((candidate) => candidate.code === 'VIEW');
      if (viewAction && this.isApplicable(component, viewAction)) {
        this.setAssignment(roleId, component, 'VIEW', true);
      }
    }

    this.enforceManageDependencies(roleId, component);
  }

  save(): void {
    const roleId = this.form.get('roleId')?.value;
    if (!roleId || !this.hasChanges) {
      this.snackBar.open(this.translate.instant('ADMIN.PERMISSIONS.NO_CHANGES'), 'Close', { duration: 2200 });
      return;
    }
    const confirmMessage = this.translate.instant('ADMIN.PERMISSIONS.CONFIRM_SAVE');
    if (!window.confirm(confirmMessage)) return;

    this.saving = true;
    this.permissionService.saveRoleAssignments(roleId, this.assignments).subscribe({
      next: () => {
        this.originalAssignments = this.assignments.map((a) => ({ ...a }));
        this.saving = false;
        this.snackBar.open('Permissions updated successfully.', 'Close', { duration: 2500 });
      },
      error: () => {
        this.saving = false;
        this.snackBar.open('Failed to save permissions.', 'Close', { duration: 3500 });
      },
    });
  }

  get hasChanges(): boolean {
    const current = new Set(this.assignments.map((a) => a.permissionCode));
    const original = new Set(this.originalAssignments.map((a) => a.permissionCode));
    if (current.size !== original.size) return true;
    for (const code of current) {
      if (!original.has(code)) return true;
    }
    return false;
  }

  private loadAssignments(roleId: string): void {
    this.permissionService.getRoleAssignments(roleId).subscribe((assignments) => {
      this.assignments = assignments;
      this.originalAssignments = assignments.map((a) => ({ ...a }));
    });
  }

  private setAssignment(roleId: string, component: AppComponentResource, actionCode: string, enabled: boolean): void {
    const permissionCode = this.permissionService.getPermissionCodeForComponent(component.code, actionCode);
    const existingIndex = this.assignments.findIndex(
      (a) => a.roleId === roleId && a.permissionCode === permissionCode
    );

    if (enabled) {
      if (existingIndex >= 0) {
        return;
      }

      this.assignments = [
        ...this.assignments,
        {
          id: `${roleId}-${component.id}-${actionCode}`,
          roleId,
          componentId: component.id,
          actionCode,
          permissionCode,
        },
      ];
      return;
    }

    if (existingIndex >= 0) {
      this.assignments = this.assignments.filter((_, index) => index !== existingIndex);
    }
  }

  private enforceManageDependencies(roleId: string, component: AppComponentResource): void {
    const manageAction = this.actions.find((action) => action.code === 'MANAGE');
    if (!manageAction || !this.isApplicable(component, manageAction)) {
      return;
    }

    const requiredApplicableActions = AdminPermissionsComponent.ManageRequiredActions
      .map((code) => this.actions.find((action) => action.code === code))
      .filter((action): action is ActionDefinition => !!action)
      .filter((action) => this.isApplicable(component, action));

    if (requiredApplicableActions.length === 0) {
      return;
    }

    const hasAllRequired = requiredApplicableActions.every((action) =>
      this.isChecked(component.id, action.code)
    );

    if (!hasAllRequired) {
      this.setAssignment(roleId, component, 'MANAGE', false);
    }
  }
}
