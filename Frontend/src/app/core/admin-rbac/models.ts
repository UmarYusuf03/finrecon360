import { PermissionCode, RoleCode } from '../auth/models';

export interface Role {
  id: string;
  code: RoleCode;
  name: string;
  description?: string;
  isSystem?: boolean;
  isActive: boolean;
}

export interface AppComponentResource {
  id: string;
  code: string;
  name: string;
  routePath: string;
  category?: string;
  description?: string;
  isActive: boolean;
}

export interface ActionDefinition {
  id: string;
  code: string;
  name: string;
  description?: string;
}

export interface PermissionAssignment {
  id: string;
  roleId: string;
  componentId: string;
  actionCode: string;
  permissionCode: PermissionCode;
}

export interface AdminUserSummary {
  id: string;
  email: string;
  displayName: string;
  isActive: boolean;
  roles: RoleCode[];
}


export interface PagedResult<T> {
  items: T[];
  totalCount: number;
  page: number;
  pageSize: number;
}
