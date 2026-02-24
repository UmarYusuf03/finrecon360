export type PermissionCode = string; // e.g. 'MATCHER.VIEW', 'ADMIN.USERS.MANAGE'
export type RoleCode = string; // e.g. 'ADMIN', 'ACCOUNTANT'

export interface CurrentUser {
  id: string;
  email: string;
  displayName: string;
  roles: RoleCode[];
  permissions: PermissionCode[];
  token: string | null;
}
