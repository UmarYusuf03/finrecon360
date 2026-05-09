export interface ImportHistoryItem {
  id: string;
  sourceType: string;
  status: string;
  importedAt: string;
  originalFileName?: string | null;
  rawRecordCount: number;
  normalizedRecordCount: number;
  errorMessage?: string | null;
  uploadedByUserId?: string | null;
  uploadedByEmail?: string | null;
  uploadedByName?: string | null;
}

export interface ImportHistoryResponse {
  items: ImportHistoryItem[];
  total: number;
  page: number;
  pageSize: number;
}

export interface ImportUploadResponse {
  id: string;
  status: string;
  sourceType: string;
  originalFileName: string;
  importedAt: string;
}

export interface ImportParseResponse {
  batchId: string;
  status: string;
  headers: string[];
  sampleRows: Record<string, string | null>[];
  parsedRowCount: number;
}

export interface SaveImportMappingRequest {
  canonicalSchemaVersion?: string;
  fieldMappings: Record<string, string>;
}

export interface ImportMappingSavedResponse {
  batchId: string;
  version: number;
  canonicalSchemaVersion: string;
  savedAt: string;
}

export interface ImportActiveTemplateResponse {
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

export interface ImportValidationError {
  rowNumber: number;
  message: string;
}

export interface ImportValidateResponse {
  batchId: string;
  status: string;
  totalRows: number;
  validRows: number;
  invalidRows: number;
  errors: ImportValidationError[];
}

export interface ImportValidationRow {
  rawRecordId: string;
  rowNumber: number;
  normalizationStatus: string;
  normalizationErrors?: string | null;
  payload: Record<string, string | null>;
}

export interface ImportValidationRowsResponse {
  batchId: string;
  totalRows: number;
  validRows: number;
  invalidRows: number;
  rows: ImportValidationRow[];
}

export interface ImportUpdateRawRecordRequest {
  payload: Record<string, string | null>;
}

export interface ReconciliationSummary {
  sourceType: string;
  workflowRoute: string;
  level3VerifiedCount: number;
  level3ExceptionCount: number;
  level4MatchedCount: number;
  level4ExceptionCount: number;
  waitingForSettlementCount: number;
  feeAdjustmentTotal: number;
  summary: string;
}

export interface ImportCommitResponse {
  batchId: string;
  status: string;
  normalizedCount: number;
  committedAt: string;
  reconciliationSummary: ReconciliationSummary;
}

export interface ImportDeleteResponse {
  batchId: string;
  fileDeleted: boolean;
  deletedAt: string;
}
