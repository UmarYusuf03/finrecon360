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
  status?: string;
  roles: RoleCode[];
}

export interface BankAccount {
  bankAccountId: string;
  bankName: string;
  accountName: string;
  accountNumber: string;
  currency: string;
  isActive: boolean;
  createdAt: string;
  updatedAt?: string | null;
}

export interface PagedResult<T> {
  items: T[];
  totalCount: number;
  page: number;
  pageSize: number;
}

export interface CanonicalField {
  field: string;
  dataType: string;
  required: boolean;
  description: string;
}

export interface CanonicalSchema {
  version: string;
  fields: CanonicalField[];
}

export interface ImportArchitectureOverview {
  totalImportBatches: number;
  totalRawRecords: number;
  totalNormalizedRecords: number;
  activeMappingTemplates: number;
  latestImportAt?: string | null;
  canonicalSchema: CanonicalSchema;
}

export interface ImportMappingTemplate {
  id: string;
  name: string;
  sourceType: string;
  canonicalSchemaVersion: string;
  version: number;
  isActive: boolean;
  mappingJson: string;
  createdAt: string;
  updatedAt?: string | null;
}
