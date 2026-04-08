import { Injectable } from '@angular/core';
import { Observable, of } from 'rxjs';
import { delay } from 'rxjs/operators';

import { DashboardData } from '../models/dashboard.models';

@Injectable({
  providedIn: 'root',
})
export class DashboardService {
  getDashboardData(): Observable<DashboardData> {
    const mock: DashboardData = {
      matcher: {
        totalTransactions: 1250,
        matched: 980,
        exceptions: 42,
      },
      balancer: {
        reconciledAccounts: 38,
        totalAccounts: 45,
        pendingReconciliations: 7,
      },
      tasks: {
        openTasks: 18,
        dueToday: 4,
        completionPercent: 72,
      },
      journal: {
        pendingApprovals: 6,
        posted: 58,
      },
      analytics: [
        { labelKey: 'DASHBOARD.ANALYTICS.CYCLE_TIME', value: '2.4d', trend: 'down' },
        { labelKey: 'DASHBOARD.ANALYTICS.MATCH_RATE', value: '88%', trend: 'up' },
        { labelKey: 'DASHBOARD.ANALYTICS.RECON_COMPLETION', value: '84%', trend: 'up' },
      ],
    };

    return of(mock).pipe(delay(200));
  }
}
