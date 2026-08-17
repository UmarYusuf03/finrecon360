export interface DashboardData {
  totalTransactions: number;
  pendingApprovalTransactions: number;
  needsBankMatchTransactions: number;
  journalReadyTransactions: number;
  totalMatchGroups: number;
  confirmedMatchGroups: number;
  pendingConfirmationMatchGroups: number;
  totalEvents: number;
  exceptionEvents: number;
  totalJournalEntries: number;
  totalBankAccounts: number;
  lastUpdatedUtc: string;
}
