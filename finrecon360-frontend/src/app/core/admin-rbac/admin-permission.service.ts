import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { BehaviorSubject, Observable, of, forkJoin } from 'rxjs';
import { filter, map, take } from 'rxjs/operators';

import { API_BASE_URL, API_ENDPOINTS, USE_MOCK_API } from '../constants/api.constants';
import {
  ActionDefinition,
  AppComponentResource,
  PermissionAssignment,
  PagedResult,
} from './models';
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

/**
 * WHY: This service acts as the central coordinator for resolving the complex many-to-many 
 * relationships between Roles, Components, and Actions. It translates flat backend 
 * permission arrays into structured assignments usable by the frontend's RBAC matrix UI.
 */
export type ScopedPermissionRow = {
  /** e.g. 'POS', 'ERP', 'GATEWAY', 'BANK' */
  sourceType: string;
  /** e.g. 'Imports', 'Reconciliation' */
  module: string;
  /** Ordered list of actions available for this module */
  actions: { code: string; label: string; permissionCode: string }[];
};

@Injectable({
  providedIn: 'root',
})
export class AdminPermissionService {
  /** WHY: Canonical source types that can appear in scoped permission codes. */
  static readonly SOURCE_TYPES = ['POS', 'ERP', 'GATEWAY', 'BANK'] as const;

  /**
   * WHY: Defines the scoped permission actions per module. These generate codes like
   * ADMIN.IMPORTS.POS.CREATE which don't fit the regular Component × Action pattern
   * so they are handled as a separate 'Scoped Permissions' section in the matrix UI.
   */
  static readonly SCOPED_MODULES: {
    module: string;
    prefix: string;
    actions: { code: string; label: string }[];
  }[] = [
    {
      module: 'Imports',
      prefix: 'ADMIN.IMPORTS',
      actions: [
        { code: 'CREATE', label: 'Upload' },
        { code: 'EDIT',   label: 'Parse / Map / Validate' },
        { code: 'COMMIT', label: 'Commit' },
      ],
    },
    {
      module: 'Reconciliation',
      prefix: 'ADMIN.RECONCILIATION',
      actions: [
        { code: 'RESOLVE',  label: 'Resolve Exceptions' },
      ],
    },
  ];

  private actions: ActionDefinition[] = [
    { id: 'act-view', code: 'VIEW', name: 'ADMIN.PERMISSIONS.ACTION_VIEW' },
    { id: 'act-view-list', code: 'VIEW_LIST', name: 'ADMIN.PERMISSIONS.ACTION_VIEW_LIST' },
    { id: 'act-create', code: 'CREATE', name: 'ADMIN.PERMISSIONS.ACTION_CREATE' },
    { id: 'act-edit', code: 'EDIT', name: 'ADMIN.PERMISSIONS.ACTION_EDIT' },
    { id: 'act-delete', code: 'DELETE', name: 'ADMIN.PERMISSIONS.ACTION_DELETE' },
    { id: 'act-approve', code: 'APPROVE', name: 'ADMIN.PERMISSIONS.ACTION_APPROVE' },
    { id: 'act-manage', code: 'MANAGE', name: 'ADMIN.PERMISSIONS.ACTION_MANAGE' },
  ];

  /**
   * WHY: Frontend UI component codes (like 'USER_MGMT') often do not 1:1 match the 
   * standardized backend permission prefixes (like 'ADMIN.USERS'). This map bridges 
   * the gap so the UI matrix can dynamically generate the correct `ADMIN.XXX.VIEW` strings.
   */
  private readonly componentPrefixOverrides: Record<string, string> = {
    USER_MGMT: 'ADMIN.USERS',
    ROLE_MGMT: 'ADMIN.ROLES',
    COMPONENT_MGMT: 'ADMIN.COMPONENTS',
    PERMISSION_MGMT: 'ADMIN.PERMISSIONS',
    DASHBOARD: 'ADMIN.DASHBOARD',
    TENANT_REG_MGMT: 'ADMIN.TENANT_REGISTRATIONS',
    TENANT_MGMT: 'ADMIN.TENANTS',
    PLAN_MGMT: 'ADMIN.PLANS',
    ENFORCEMENT_MGMT: 'ADMIN.ENFORCEMENT',
    IMPORT_WORKBENCH_MGMT: 'ADMIN.IMPORT_WORKBENCH',
    IMPORT_ARCHITECTURE_MGMT: 'ADMIN.IMPORT_ARCHITECTURE',
    BANK_ACCOUNTS_MGMT: 'ADMIN.BANK_ACCOUNTS',
    AUDIT_LOGS_MGMT: 'ADMIN.AUDIT_LOGS',
  };

  private readonly actionsSubject = new BehaviorSubject<ActionDefinition[]>([]);
  private actionsLoaded = false;

  constructor(
    private http: HttpClient,
    private componentService: AdminComponentService,
  ) {}

  getActions(): Observable<ActionDefinition[]> {
    if (USE_MOCK_API) {
      return of(this.actions);
    }

    if (!this.actionsLoaded) {
      this.actionsLoaded = true;
      this.http
        .get<PagedResult<ActionDto>>(
          `${API_BASE_URL}${API_ENDPOINTS.ADMIN.ACTIONS}?page=1&pageSize=100`,
        )
        .pipe(
          map((result) =>
            result.items
              .filter((action) => action.isActive)
              .map((action) => ({
                id: action.id,
                code: action.code,
                name: action.name,
                description: action.description ?? undefined,
              })),
          ),
        )
        .subscribe((actions) => this.actionsSubject.next(actions));
    }

    return this.actionsSubject.asObservable();
  }

  /**
   * WHY: Getting a role's assignments requires knowing all possible components and actions 
   * to build the visual matrix. We `forkJoin` here rather than having the UI components 
   * orchestrate it to ensure the service remains the single source of truth for the RBAC state.
   */
  getRoleAssignments(roleId: string): Observable<PermissionAssignment[]> {
    if (USE_MOCK_API) {
      return of([]);
    }

    return forkJoin({
      components: this.componentService.getComponents().pipe(
        filter((components) => components.length > 0),
        take(1),
        map((components) => components.filter((component) => component.isActive)),
      ),
      actions: this.getActions().pipe(
        filter((actions) => actions.length > 0),
        take(1),
      ),
      role: this.http.get<RoleDetailDto>(`${API_BASE_URL}${API_ENDPOINTS.ADMIN.ROLES}/${roleId}`),
    }).pipe(
      map(({ components, actions, role }) =>
        this.buildAssignments(role.permissions, components, actions, roleId),
      ),
    );
  }

  /**
   * WHY: Returns the raw set of all permission codes currently assigned to a role.
   * This is used by the scoped-permissions section to pre-populate checkboxes, because
   * scoped codes (ADMIN.IMPORTS.POS.CREATE) are not visible to buildAssignments which
   * only handles Component×Action codes.
   */
  getRolePermissionCodes(roleId: string): Observable<Set<string>> {
    if (USE_MOCK_API) {
      return of(new Set<string>());
    }
    return this.http
      .get<RoleDetailDto>(`${API_BASE_URL}${API_ENDPOINTS.ADMIN.ROLES}/${roleId}`)
      .pipe(map((role) => new Set(role.permissions.map((p) => p.code))));
  }

  getAvailablePermissionCodes(): Observable<Set<string>> {
    if (USE_MOCK_API) {
      const codes = new Set<string>();
      // Standard component × action codes
      Object.values(this.componentPrefixOverrides).forEach((prefix) => {
        this.actions.forEach((action) => {
          codes.add(`${prefix}.${action.code}`);
        });
      });
      // WHY: Also include scoped codes so mock mode shows the full scoped section.
      for (const src of AdminPermissionService.SOURCE_TYPES) {
        for (const mod of AdminPermissionService.SCOPED_MODULES) {
          for (const act of mod.actions) {
            codes.add(`${mod.prefix}.${src}.${act.code}`);
          }
        }
      }
      return of(codes);
    }

    return this.http
      .get<PagedResult<PermissionDto>>(`${API_BASE_URL}${API_ENDPOINTS.ADMIN.PERMISSIONS}?page=1&pageSize=500`)
      .pipe(map((result) => new Set(result.items.map((permission) => permission.code))));
  }

  /**
   * WHY: Returns all possible scoped permission rows for the UI section.
   * Each row = one sourceType (e.g. 'POS') with all module actions as checkboxes.
   * The component tracks which are toggled via isScopedAssigned() / toggleScopedPermission().
   */
  getScopedPermissionRows(): ScopedPermissionRow[] {
    const rows: ScopedPermissionRow[] = [];
    for (const src of AdminPermissionService.SOURCE_TYPES) {
      for (const mod of AdminPermissionService.SCOPED_MODULES) {
        rows.push({
          sourceType: src,
          module: mod.module,
          actions: mod.actions.map((a) => ({
            code: a.code,
            label: a.label,
            permissionCode: `${mod.prefix}.${src}.${a.code}`,
          })),
        });
      }
    }
    return rows;
  }

  /**
   * WHY: Provides a safe abstraction for UI components to determine the literal string used 
   * by the backend for a given route/action, protecting components from needing to know 
   * about `componentPrefixOverrides`.
   */
  getPermissionCodeForComponent(componentCode: string, actionCode: string): string {
    const prefix = this.componentPrefixOverrides[componentCode] ?? componentCode;
    return `${prefix}.${actionCode}`;
  }

  saveRoleAssignments(roleId: string, assignments: PermissionAssignment[], scopedCodes: string[] = []): Observable<void> {
    if (USE_MOCK_API) {
      return of(void 0);
    }

    // WHY: Merge standard component×action codes + scoped source-type codes into a single
    // PUT call. The backend's ReplaceRolePermissions endpoint accepts both by code.
    const standardCodes = assignments.map((assignment) => assignment.permissionCode);
    const allCodes = [...new Set([...standardCodes, ...scopedCodes])];
    return this.http.put<void>(
      `${API_BASE_URL}${API_ENDPOINTS.ADMIN.ROLES}/${roleId}/permissions`,
      { permissionCodes: allCodes },
    );
  }

  /**
   * WHY: The backend typically returns a flat list of `PermissionDto` objects assigned to a role. 
   * The matrix UI requires an object per cell (Component x Action). This method crosses 
   * the domains to generate assignments ONLY for the intersection of authorized permissions.
   */
  private buildAssignments(
    permissions: PermissionDto[],
    components: AppComponentResource[],
    actions: ActionDefinition[],
    roleId: string,
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
