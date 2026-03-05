export type PermissionCode = string; // e.g. 'MATCHER.VIEW', 'ADMIN.USERS.MANAGE'
export type RoleCode = string; // e.g. 'ADMIN', 'ACCOUNTANT'

export interface CurrentUser {
  id: string;
  email: string;
  displayName: string;
  status?: string;
  tenantId?: string | null;
  tenantName?: string | null;
  tenantStatus?: string | null;
  roles: RoleCode[];
  permissions: PermissionCode[];
  token: string | null;
}
