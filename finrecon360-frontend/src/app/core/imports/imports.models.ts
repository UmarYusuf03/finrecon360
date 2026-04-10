export interface ImportHistoryItem {
  id: string;
  sourceType: string;
  status: string;
  importedAt: string;
  originalFileName?: string | null;
  rawRecordCount: number;
  normalizedRecordCount: number;
  errorMessage?: string | null;
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

export interface ImportCommitResponse {
  batchId: string;
  status: string;
  normalizedCount: number;
  committedAt: string;
}

export interface ImportDeleteResponse {
  batchId: string;
  fileDeleted: boolean;
  deletedAt: string;
}
