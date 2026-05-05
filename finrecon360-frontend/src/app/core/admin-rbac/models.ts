/**
 * WHY: These models define the structural contracts for the Role-Based Access Control (RBAC) 
 * mechanism in the admin portal. They mirror the core domain entities used by the backend 
 * to ensure that frontend components always operate on a strictly defined shape of data, 
 * preventing runtime errors when evaluating and assigning user permissions.
 */
import { PermissionCode, RoleCode } from '../auth/models';

/**
 * WHY: Defines the shape of a Role within the system. Roles are the primary mechanism for grouping
 * collections of permissions. `isSystem` is used to prevent the deletion or mutation of core roles
 * (like Super Admin) that are required for the platform to function baseline.
 */
export interface Role {
  id: string;
  code: RoleCode;
  name: string;
  description?: string;
  isSystem?: boolean;
  isActive: boolean;
}

/**
 * WHY: Represents a logical component or feature module within the application (e.g., 'Dashboard', 'Reports'). 
 * Permissions are assigned against these components. The `routePath` is included to potentially automate 
 * route-guarding based on component access.
 */
export interface AppComponentResource {
  id: string;
  code: string;
  name: string;
  routePath: string;
  category?: string;
  description?: string;
  isActive: boolean;
}

/**
 * WHY: Granular actions that can be performed on an `AppComponentResource` (e.g., 'View', 'Edit', 'Delete').
 * Keeping these as separate definitions allows actions to be reused across different components.
 */
export interface ActionDefinition {
  id: string;
  code: string;
  name: string;
  description?: string;
}

/**
 * WHY: The intersection relationship mapping a Role to a specific Action on a specific Component. 
 * This is the atomic unit of the permission matrix used to resolve `hasPermission` checks.
 */
export interface PermissionAssignment {
  id: string;
  roleId: string;
  componentId: string;
  actionCode: string;
  permissionCode: PermissionCode;
}

/**
 * WHY: A read-optimized view of a user focused strictly on administration and identity mapping.
 * Avoids pulling down the entire aggregate root of a user just for lists or simple summaries.
 */
export interface AdminUserSummary {
  id: string;
  email: string;
  displayName: string;
  isActive: boolean;
  status?: string;
  roles: RoleCode[];
}

/**
 * WHY: Standardized wrapper for paginated endpoints to ensure uniform client-side handling 
 * of infinite scrolling or standard pagination UI components across all admin tables.
 */
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

export interface Transaction {
  transactionId: string;
  amount: number;
  transactionDate: string;
  referenceNumber?: string | null;
  description: string;
  bankAccountId?: string | null;
  transactionType: string;
  paymentMethod: string;
  transactionState: string;
  createdByUserId?: string | null;
  approvedAt?: string | null;
  approvedByUserId?: string | null;
  rejectedAt?: string | null;
  rejectedByUserId?: string | null;
  rejectionReason?: string | null;
  createdAt: string;
  updatedAt?: string | null;
}

export interface TransactionStateHistory {
  transactionStateHistoryId: string;
  transactionId: string;
  fromState: string;
  toState: string;
  changedByUserId?: string | null;
  changedAt: string;
  note?: string | null;
}

/**
 * WHY: Enriched NeedsBankMatch row — combines core transaction fields with matched
 * GATEWAY import record context and any existing ReconciliationMatchGroup so the
 * accountant can see the full payment chain (ERP → Gateway → Bank) in one place.
 */
export interface NeedsBankMatchRecord {
  // Core transaction
  transactionId: string;
  amount: number;
  transactionDate: string;
  description: string;
  bankAccountId?: string | null;
  transactionType: string;
  paymentMethod: string;
  transactionState: string;
  createdByUserId?: string | null;
  approvedAt?: string | null;
  createdAt: string;
  // Linked import record context
  importedNormalizedRecordId?: string | null;
  importSourceType?: string | null;
  referenceNumber?: string | null;
  accountCode?: string | null;
  grossAmount?: number | null;
  processingFee?: number | null;
  netImportAmount: number;
  settlementId?: string | null;
  matchStatus: string;
  // Match group context
  reconciliationMatchGroupId?: string | null;
  matchLevel?: string | null;
  isConfirmed: boolean;
  isJournalPosted: boolean;
  matchMetadataJson?: string | null;
}

export interface CreateTransactionRequest {
  amount: number;
  transactionDate: string;
  description: string;
  bankAccountId?: string | null;
  transactionType: string;
  paymentMethod: string;
}

export interface UpdateTransactionRequest {
  amount: number;
  transactionDate: string;
  referenceNumber?: string | null;
  description: string;
  bankAccountId?: string | null;
  transactionType: string;
  paymentMethod: string;
}

export interface ApproveTransactionRequest {
  note?: string | null;
}

export interface RejectTransactionRequest {
  reason: string;
}
export interface PagedResult<T> {
  items: T[];
  totalCount: number;
  page: number;
  pageSize: number;
}

/**
 * WHY: Describes an individual field within the internal normalization target (Canonical).
 * Essential for the data mapping engine to know data types and validation requirements.
 */
export interface CanonicalField {
  field: string;
  dataType: string;
  required: boolean;
  description: string;
}

/**
 * WHY: The root definition of the canonical format for a given version. Data imports must ultimately
 * map to this schema version to be ingested successfully by downstream reconciliation logic.
 */
export interface CanonicalSchema {
  version: string;
  fields: CanonicalField[];
}

/**
 * WHY: A high-level metric view used by the admin dashboard to give quick insights into 
 * data processing health, throughput, and the current state of mappings.
 */
export interface ImportArchitectureOverview {
  totalImportBatches: number;
  totalRawRecords: number;
  totalNormalizedRecords: number;
  activeMappingTemplates: number;
  latestImportAt?: string | null;
  canonicalSchema: CanonicalSchema;
}

/**
 * WHY: Represents explicit rules for how raw ingestion sources map fields to the `CanonicalSchema`. 
 * Versioning allows historical tracking and safe rollback of template changes.
 */
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

// ─── Reconciliation Domain ───────────────────────────────────────────────────

export type MatchStatus =
  | 'PENDING'
  | 'INTERNAL_VERIFIED'
  | 'SALES_VERIFIED'
  | 'EXCEPTION'
  | 'WAITING'
  | 'MATCHED';

export interface ReconciliationMatchedRecord {
  reconciliationMatchedRecordId: string;
  importedNormalizedRecordId: string;
  sourceType: string;
  matchAmount: number;
  transactionDate?: string | null;
  referenceNumber?: string | null;
  grossAmount?: number | null;
  processingFee?: number | null;
  netAmount: number;
  currency: string;
  matchStatus: MatchStatus;
}

export interface ReconciliationMatchGroup {
  reconciliationMatchGroupId: string;
  importBatchId: string;
  matchLevel: string;
  settlementKey?: string | null;
  isConfirmed: boolean;
  confirmedByUserId?: string | null;
  confirmedAt?: string | null;
  isJournalPosted: boolean;
  createdAt: string;
  updatedAt?: string | null;
  matchedRecords: ReconciliationMatchedRecord[];
}

export interface ReconciliationEvent {
  reconciliationEventId: string;
  importBatchId: string;
  importedNormalizedRecordId: string;
  eventType: string;
  stage: string;
  sourceType: string;
  status: string;
  detailJson?: string | null;
  createdAt: string;
  resolvedAt?: string | null;
}

export interface WaitingRecord {
  importedNormalizedRecordId: string;
  importBatchId: string;
  transactionDate: string;
  referenceNumber?: string | null;
  description?: string | null;
  grossAmount?: number | null;
  processingFee?: number | null;
  netAmount: number;
  currency: string;
  matchStatus: MatchStatus;
}

export interface JournalEntry {
  journalEntryId: string;
  transactionId?: string | null;
  reconciliationMatchGroupId?: string | null;
  entryType: string;
  amount: number;
  currency: string;
  postedAt: string;
  postedByUserId?: string | null;
  notes?: string | null;
}

export interface AttachSettlementIdRequest {
  settlementId: string;
}

export interface PostJournalRequest {
  notes?: string | null;
}
