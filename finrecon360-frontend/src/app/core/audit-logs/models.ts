import { PagedResult } from '../admin-rbac/models';

export interface AuditLogItem {
  auditLogId: string;
  userId: string | null;
  action: string;
  entity: string | null;
  entityId: string | null;
  metadata: string | null;
  createdAt: string;
  userEmail?: string | null;
  userDisplayName?: string | null;
}

export interface AuditLogFilters {
  page: number;
  pageSize: number;
  action?: string;
  entity?: string;
  userId?: string;
  fromUtc?: string;
  toUtc?: string;
  search?: string;
}

export type AuditLogPage = PagedResult<AuditLogItem>;
