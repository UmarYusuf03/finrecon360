export interface MatcherStats {
  totalTransactions: number;
  matched: number;
  exceptions: number;
}

export interface BalancerStats {
  reconciledAccounts: number;
  totalAccounts: number;
  pendingReconciliations: number;
}

export interface TaskStats {
  openTasks: number;
  dueToday: number;
  completionPercent: number;
}

export interface JournalStats {
  pendingApprovals: number;
  posted: number;
}

export interface AnalyticsKpi {
  labelKey: string;
  value: string;
  trend?: 'up' | 'down' | 'flat';
}

export interface DashboardData {
  matcher: MatcherStats;
  balancer: BalancerStats;
  tasks: TaskStats;
  journal: JournalStats;
  analytics: AnalyticsKpi[];
}
