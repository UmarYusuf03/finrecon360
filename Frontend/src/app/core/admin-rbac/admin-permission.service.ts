import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable, of, forkJoin } from 'rxjs';
import { map, take } from 'rxjs/operators';

import { API_BASE_URL, API_ENDPOINTS, USE_MOCK_API } from '../constants/api.constants';
import { ActionDefinition, AppComponentResource, PermissionAssignment, PagedResult } from './models';
import { AdminComponentService } from './admin-component.service';

interface ActionDto {
  id: string;
  code: string;
  name: string;
  description?: string | null;
  isActive: boolean;
}

interface RoleDetailDto {
  id: string;
  code: string;
  name: string;
  description?: string | null;
  isSystem: boolean;
  isActive: boolean;
  permissions: PermissionDto[];
}

interface PermissionDto {
  id: string;
  code: string;
  name: string;
  description?: string | null;
  module?: string | null;
}

@Injectable({
  providedIn: 'root',
})
export class AdminPermissionService {
  private actions: ActionDefinition[] = [
    { id: 'act-view', code: 'VIEW', name: 'ADMIN.PERMISSIONS.ACTION_VIEW' },
    { id: 'act-view-list', code: 'VIEW_LIST', name: 'ADMIN.PERMISSIONS.ACTION_VIEW_LIST' },
    { id: 'act-create', code: 'CREATE', name: 'ADMIN.PERMISSIONS.ACTION_CREATE' },
    { id: 'act-edit', code: 'EDIT', name: 'ADMIN.PERMISSIONS.ACTION_EDIT' },
    { id: 'act-delete', code: 'DELETE', name: 'ADMIN.PERMISSIONS.ACTION_DELETE' },
    { id: 'act-approve', code: 'APPROVE', name: 'ADMIN.PERMISSIONS.ACTION_APPROVE' },
    { id: 'act-manage', code: 'MANAGE', name: 'ADMIN.PERMISSIONS.ACTION_MANAGE' },
  ];

  private readonly componentPrefixOverrides: Record<string, string> = {
    USER_MGMT: 'ADMIN.USERS',
    ROLE_MGMT: 'ADMIN.ROLES',
    PERMISSION_MGMT: 'ADMIN.PERMISSIONS',
    DASHBOARD: 'ADMIN.DASHBOARD',
  };

  constructor(private http: HttpClient, private componentService: AdminComponentService) {}

  getActions(): Observable<ActionDefinition[]> {
    if (USE_MOCK_API) {
      return of(this.actions);
    }

    return this.http
      .get<PagedResult<ActionDto>>(`${API_BASE_URL}${API_ENDPOINTS.ADMIN.ACTIONS}?page=1&pageSize=100`)
      .pipe(
        map((result) =>
          result.items
            .filter((action) => action.isActive)
            .map((action) => ({
              id: action.id,
              code: action.code,
              name: action.name,
              description: action.description ?? undefined,
            }))
        )
      );
  }

  getRoleAssignments(roleId: string): Observable<PermissionAssignment[]> {
    if (USE_MOCK_API) {
      return of([]);
    }

    return forkJoin({
      components: this.componentService.getComponents().pipe(
        take(1),
        map((components) => components.filter((c) => c.isActive))
      ),
      actions: this.getActions().pipe(take(1)),
      role: this.http.get<RoleDetailDto>(`${API_BASE_URL}${API_ENDPOINTS.ADMIN.ROLES}/${roleId}`),
    }).pipe(
      map(({ components, actions, role }) => this.buildAssignments(role.permissions, components, actions, roleId))
    );
  }

  getPermissionCodeForComponent(componentCode: string, actionCode: string): string {
    const prefix = this.componentPrefixOverrides[componentCode] ?? componentCode;
    return `${prefix}.${actionCode}`;
  }

  saveRoleAssignments(roleId: string, assignments: PermissionAssignment[]): Observable<void> {
    if (USE_MOCK_API) {
      return of(void 0);
    }

    const permissionCodes = assignments.map((assignment) => assignment.permissionCode);
    return this.http.put<void>(`${API_BASE_URL}${API_ENDPOINTS.ADMIN.ROLES}/${roleId}/permissions`, {
      permissionCodes,
    });
  }

  private buildAssignments(
    permissions: PermissionDto[],
    components: AppComponentResource[],
    actions: ActionDefinition[],
    roleId: string
  ): PermissionAssignment[] {
    const permissionCodes = new Set(permissions.map((permission) => permission.code));
    const assignments: PermissionAssignment[] = [];

    components.forEach((component) => {
      const prefix = this.componentPrefixOverrides[component.code] ?? component.code;
      actions.forEach((action) => {
        const permissionCode = `${prefix}.${action.code}`;
        if (permissionCodes.has(permissionCode)) {
          assignments.push({
            id: `${roleId}-${component.id}-${action.code}`,
            roleId,
            componentId: component.id,
            actionCode: action.code,
            permissionCode,
          });
        }
      });
    });

    return assignments;
  }
}
