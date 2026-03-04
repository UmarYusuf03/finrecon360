export interface AdminRole {
  code: string;
  name: string;
  description: string;
  permissions: string[];
}

export interface AdminComponentResource {
  code: string;
  name: string;
  actions: string[];
}

export interface AdminPermissionMatrixRow {
  role: string;
  component: string;
  actions: string[];
}

export interface AdminUser {
  id: string;
  name: string;
  email: string;
  roles: string[];
  status: 'active' | 'disabled';
}
