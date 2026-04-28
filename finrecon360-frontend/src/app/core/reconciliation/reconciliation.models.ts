export interface RunMatchingRequest {
  bankStatementImportId: string;
}

export interface BankAccount {
  id: string;
  accountName: string;
  accountNumber: string;
}

export interface BankStatementImport {
  importId: string;
  importDate: string;
  bankAccountId: string;
  status: string;
  totalLinesImported: number;
}

export interface PaginatedResponse<T> {
  items: T[];
  totalCount: number;
  pageNumber: number;
  pageSize: number;
}

export interface MatchingSummaryResponse {
  totalLinesProcessed: number;
  matchesFound: number;
  generalLedgerMatches: number;
  invoiceMatches: number;
  payoutMatches: number;
}
