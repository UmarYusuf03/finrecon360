import { Component } from '@angular/core';
import { CommonModule } from '@angular/common';
import { MatIconModule } from '@angular/material/icon';
import { RouterLink } from '@angular/router';
import { TranslateModule } from '@ngx-translate/core';

interface ReportLink {
  path: string;
  icon: string;
  titleKey: string;
  copyKey: string;
}

@Component({
  selector: 'app-admin-reports-hub',
  standalone: true,
  imports: [CommonModule, MatIconModule, RouterLink, TranslateModule],
  templateUrl: './admin-reports-hub.html',
  styleUrls: ['./admin-reports-hub.scss'],
})
export class AdminReportsHubComponent {
  readonly links: ReportLink[] = [
    {
      path: '/app/admin/financial-reports',
      icon: 'account_balance',
      titleKey: 'FINANCIAL_REPORTS.TITLE',
      copyKey: 'REPORTS_HUB.FINANCIAL_REPORTS_COPY',
    },
    {
      path: '/app/matcher/trends',
      icon: 'trending_up',
      titleKey: 'MATCHER.TRENDS.TITLE',
      copyKey: 'REPORTS_HUB.RECONCILIATION_TRENDS_COPY',
    },
    {
      path: '/app/admin/cash-flow-forecast',
      icon: 'timeline',
      titleKey: 'CASH_FLOW.TITLE',
      copyKey: 'REPORTS_HUB.CASH_FLOW_COPY',
    },
    {
      path: '/app/dashboard',
      icon: 'dashboard',
      titleKey: 'REPORTS_HUB.DASHBOARD_TITLE',
      copyKey: 'REPORTS_HUB.DASHBOARD_COPY',
    },
    {
      path: '/app/admin/report-schedules',
      icon: 'schedule_send',
      titleKey: 'REPORT_SCHEDULES.TITLE',
      copyKey: 'REPORTS_HUB.REPORT_SCHEDULES_COPY',
    },
  ];
}
