import { Component, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { MatTabsModule } from '@angular/material/tabs';
import {
  ActivatedRoute,
  Router,
  RouterLink,
  RouterLinkActive,
  RouterOutlet,
} from '@angular/router';
import { TranslateModule } from '@ngx-translate/core';
import { Observable } from 'rxjs';
import { filter, map, switchMap, take } from 'rxjs/operators';

import { AuthService } from '../../../core/auth/auth.service';

type AdminLink = {
  path: string;
  label: string;
  /**
   * Sentence shown on the overview card. Follows the existing i18n convention where a section's
   * heading is X.TITLE and its one-line explanation is X.COPY, so no new strings are needed.
   */
  description: string;
  permission: string;
  scope: 'tenant' | 'system';
  role?: string;
};

@Component({
  selector: 'app-admin-shell',
  standalone: true,
  imports: [
    CommonModule,
    RouterOutlet,
    RouterLink,
    RouterLinkActive,
    MatTabsModule,
    TranslateModule,
  ],
  templateUrl: './admin-shell.html',
  styleUrls: ['./admin-shell.scss'],
})
export class AdminShellComponent implements OnInit {
  private readonly links: AdminLink[] = [
    {
      path: '/app/admin/reports',
      label: 'REPORTS_HUB.TITLE',
      description: 'REPORTS_HUB.COPY',
      permission: 'ADMIN.DASHBOARD.VIEW',
      scope: 'tenant',
    },
    {
      path: '/app/admin/bank-accounts',
      label: 'BANK_ACCOUNTS.TITLE',
      description: 'BANK_ACCOUNTS.COPY',
      permission: 'ADMIN.BANK_ACCOUNTS.VIEW',
      scope: 'tenant',
    },
    {
      path: '/app/admin/cash-flow-forecast',
      label: 'CASH_FLOW.TITLE',
      description: 'CASH_FLOW.COPY',
      permission: 'ADMIN.CASH_FLOW_FORECAST.VIEW',
      scope: 'tenant',
    },
    {
      path: '/app/admin/financial-reports',
      label: 'FINANCIAL_REPORTS.TITLE',
      description: 'FINANCIAL_REPORTS.COPY',
      permission: 'ADMIN.FINANCIAL_REPORTS.VIEW',
      scope: 'tenant',
    },
    {
      path: '/app/admin/report-schedules',
      label: 'REPORT_SCHEDULES.TITLE',
      description: 'REPORT_SCHEDULES.SUBTITLE',
      permission: 'ADMIN.REPORT_SCHEDULES.MANAGE',
      scope: 'tenant',
    },
    {
      path: '/app/admin/subscription',
      label: 'PROFILE.BILLING.TITLE',
      description: 'PROFILE.BILLING.COPY',
      permission: 'ADMIN.SUBSCRIPTIONS.MANAGE',
      scope: 'tenant',
    },
    {
      path: '/app/admin/roles',
      label: 'ADMIN.ROLES.TITLE',
      description: 'ADMIN.ROLES.COPY',
      permission: 'ADMIN.ROLES.VIEW',
      scope: 'tenant',
    },
    {
      path: '/app/admin/components',
      label: 'ADMIN.COMPONENTS.TITLE',
      description: 'ADMIN.COMPONENTS.COPY',
      permission: 'ADMIN.COMPONENTS.VIEW',
      scope: 'tenant',
    },
    {
      path: '/app/admin/permissions',
      label: 'ADMIN.PERMISSIONS.TITLE',
      description: 'ADMIN.PERMISSIONS.COPY',
      permission: 'ADMIN.PERMISSIONS.VIEW',
      scope: 'tenant',
    },
    {
      path: '/app/admin/users',
      label: 'ADMIN.USERS.TITLE',
      description: 'ADMIN.USERS.COPY',
      permission: 'ADMIN.USERS.VIEW',
      scope: 'tenant',
    },
    {
      path: '/app/admin/audit-logs',
      label: 'ADMIN.TENANT_AUDIT_LOGS.TITLE',
      description: 'ADMIN.TENANT_AUDIT_LOGS.COPY',
      permission: 'ADMIN.AUDIT_LOGS.VIEW',
      scope: 'tenant',
    },
    {
      path: '/app/system/tenant-registrations',
      label: 'ADMIN.TENANT_REGISTRATIONS.TITLE',
      description: 'ADMIN.TENANT_REGISTRATIONS.COPY',
      permission: 'ADMIN.TENANT_REGISTRATIONS.MANAGE',
      scope: 'system',
    },
    {
      path: '/app/system/tenants',
      label: 'ADMIN.TENANTS.TITLE',
      description: 'ADMIN.TENANTS.COPY',
      permission: 'ADMIN.TENANTS.MANAGE',
      scope: 'system',
    },
    {
      path: '/app/system/plans',
      label: 'ADMIN.PLANS.TITLE',
      description: 'ADMIN.PLANS.COPY',
      permission: 'ADMIN.PLANS.MANAGE',
      scope: 'system',
    },
    {
      path: '/app/system/payment-alerts',
      label: 'ADMIN.PAYMENT_ALERTS.TITLE',
      description: 'ADMIN.PAYMENT_ALERTS.COPY',
      permission: 'ADMIN.PAYMENT_ALERTS.VIEW',
      scope: 'system',
    },
    {
      path: '/app/system/enforcement',
      label: 'ADMIN.ENFORCEMENT.TITLE',
      description: 'ADMIN.ENFORCEMENT.COPY',
      permission: 'ADMIN.ENFORCEMENT.MANAGE',
      scope: 'system',
    },
    {
      path: '/app/system/audit-logs',
      label: 'ADMIN.AUDIT_LOGS.TITLE',
      description: 'ADMIN.AUDIT_LOGS.COPY',
      permission: 'ADMIN.TENANTS.MANAGE',
      scope: 'system',
      role: 'ADMIN',
    },
  ];

  readonly visibleLinks$: Observable<AdminLink[]>;
  private readonly scope: 'tenant' | 'system';
  constructor(
    private authService: AuthService,
    private router: Router,
    private route: ActivatedRoute,
  ) {
    this.scope = (this.route.snapshot.data['scope'] as 'tenant' | 'system' | undefined) ?? 'tenant';
    this.visibleLinks$ = this.authService.currentUser$.pipe(
      map((user) => {
        if (!user) {
          return [] as AdminLink[];
        }

        if (this.scope === 'system' && !user.isSystemAdmin) {
          return [] as AdminLink[];
        }

        if (this.scope === 'tenant' && user.isSystemAdmin) {
          return [] as AdminLink[];
        }

        return this.links.filter((link) => {
          if (link.scope !== this.scope) {
            return false;
          }

          if (!this.hasPermission(user.permissions, link.permission)) {
            return false;
          }

          return this.hasRole(user.roles, link.role);
        });
      }),
    );
  }

  ngOnInit(): void {
    this.authService.currentUser$
      .pipe(
        filter((user) => !!user),
        take(1),
        switchMap(() => this.visibleLinks$.pipe(take(1))),
      )
      .subscribe((links) => {
        const onScopeRoot =
          this.router.url === `/app/${this.scope}` || this.router.url === `/app/${this.scope}/`;
        if (!onScopeRoot) {
          return;
        }

        // Previously this jumped straight to links[0], so the administration area opened on
        // whichever section happened to be first. The overview grid is the landing now, so the
        // only redirect left is the genuine no-access case.
        if (links.length === 0) {
          this.router.navigate(['/app/not-authorized'], { relativeTo: this.route.root });
        }
      });
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

  private hasRole(grantedRoles: string[], requiredRole?: string): boolean {
    if (!requiredRole) {
      return true;
    }

    return grantedRoles.includes(requiredRole);
  }
}
