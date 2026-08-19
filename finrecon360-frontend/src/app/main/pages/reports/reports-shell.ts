import { CommonModule } from '@angular/common';
import { Component } from '@angular/core';
import { MatTabsModule } from '@angular/material/tabs';
import { RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { TranslateModule } from '@ngx-translate/core';
import { Observable } from 'rxjs';
import { map } from 'rxjs/operators';

import { AuthService } from '../../../core/auth/auth.service';

type ReportLink = {
  path: string;
  label: string;
  permission: string;
};

@Component({
  selector: 'app-reports-shell',
  standalone: true,
  imports: [CommonModule, RouterOutlet, RouterLink, RouterLinkActive, MatTabsModule, TranslateModule],
  templateUrl: './reports-shell.html',
  styleUrls: ['./reports-shell.scss'],
})
export class ReportsShellComponent {
  private readonly links: ReportLink[] = [
    {
      path: '/app/reports',
      label: 'REPORTS_HUB.TITLE',
      permission: 'ADMIN.DASHBOARD.VIEW',
    },
    {
      path: '/app/reports/financial-reports',
      label: 'FINANCIAL_REPORTS.TITLE',
      permission: 'ADMIN.FINANCIAL_REPORTS.VIEW',
    },
    {
      path: '/app/reports/cash-flow-forecast',
      label: 'CASH_FLOW.TITLE',
      permission: 'ADMIN.CASH_FLOW_FORECAST.VIEW',
    },
    {
      path: '/app/reports/report-schedules',
      label: 'REPORT_SCHEDULES.TITLE',
      permission: 'ADMIN.REPORT_SCHEDULES.MANAGE',
    },
  ];

  readonly visibleLinks$: Observable<ReportLink[]>;

  constructor(private readonly authService: AuthService) {
    this.visibleLinks$ = this.authService.currentUser$.pipe(
      map((user) => {
        if (!user || user.isSystemAdmin) {
          return [] as ReportLink[];
        }

        return this.links.filter((link) => this.hasPermission(user.permissions, link.permission));
      }),
    );
  }

  private hasPermission(grantedPermissions: string[], requiredPermission: string): boolean {
    if (grantedPermissions.includes(requiredPermission)) {
      return true;
    }

    const separatorIndex = requiredPermission.lastIndexOf('.');
    if (separatorIndex <= 0) {
      return false;
    }

    const manageCode = `${requiredPermission.slice(0, separatorIndex)}.MANAGE`;
    return grantedPermissions.includes(manageCode);
  }
}
