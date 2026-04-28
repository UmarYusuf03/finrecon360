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

export interface BankStatementLine {
  id: string;
  bankStatementImportId: string;
  transactionDate: string;
  postingDate?: string | null;
  referenceNumber?: string | null;
  description?: string | null;
  amount: number;
  isReconciled: boolean;
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

export interface MatchDecisionDto {
  id: string;
  matchGroupId: string;
  decision: string;
  decisionReason?: string;
  decidedBy: string;
  decidedAt: string;
  bankLineDescription?: string;
  systemEntityDescription?: string;
  amount?: number;
  matchType?: string;
}

export interface MatchGroup {
  id: string;
  reconciliationRunId: string;
  matchConfidenceScore?: number;
  status: string;
  matchDecisions: MatchDecisionDto[];
}

export interface ConfirmMatchesResponse {
  totalConfirmed: number;
  totalReconciliationsFinalized: number;
}
