import { Routes } from '@angular/router';

import { AccessGuard } from '../core/auth/access.guard';
import { AuthGuard } from '../core/auth/auth.guard';
import { ShellComponent } from './layout/shell/shell';
import { AdminShellComponent } from './pages/admin/admin-shell';
import { AdminComponentsComponent } from './pages/admin/admin-components';
import { AdminPermissionsComponent } from './pages/admin/admin-permissions';
import { AdminRolesComponent } from './pages/admin/admin-roles';
import { AdminUsersComponent } from './pages/admin/admin-users';
import { DashboardComponent } from './pages/dashboard/dashboard';
import { ImportsShellComponent } from './pages/imports/imports-shell';
import { MatcherPageComponent } from './pages/matcher/matcher-page';
import { MatcherShellComponent } from './pages/matcher/matcher-shell';
import { NotAuthorizedComponent } from './pages/not-authorized/not-authorized';
import { ProfileComponent } from './pages/profile/profile';
import { ReportsShellComponent } from './pages/reports/reports-shell';

export const mainRoutes: Routes = [
  {
    path: '',
    component: ShellComponent,
    canActivate: [AuthGuard],
    children: [
      {
        path: 'dashboard',
        component: DashboardComponent,
        canActivate: [AccessGuard],
        data: { scope: 'tenant', permissions: ['ADMIN.DASHBOARD.VIEW'] },
      },
      {
        path: 'admin',
        component: AdminShellComponent,
        canActivate: [AccessGuard],
        data: {
          scope: 'tenant',
          anyPermissions: [
            'ADMIN.BANK_ACCOUNTS.VIEW',
            'ADMIN.TRANSACTIONS.VIEW',
            'ADMIN.ROLES.VIEW',
            'ADMIN.COMPONENTS.VIEW',
            'ADMIN.PERMISSIONS.VIEW',
            'ADMIN.USERS.VIEW',
            'ADMIN.IMPORT_ARCHITECTURE.VIEW',
          ],
        },
        children: [
          {
            // Reporting moved out of Admin into its own top-level module (see 'reports' below) so
            // that visiting it no longer highlights the Admin tab or shows Admin's sub-nav. Kept as
            // a redirect for old bookmarks/links.
            path: 'reports',
            pathMatch: 'full',
            redirectTo: '/app/reports',
          },
          {
            path: 'report-schedules',
            pathMatch: 'full',
            redirectTo: '/app/reports/report-schedules',
          },
          {
            path: 'transactions',
            pathMatch: 'full',
            redirectTo: '/app/transactions',
          },
          {
            path: 'journal-ready',
            pathMatch: 'full',
            redirectTo: '/app/transactions/journal-ready',
          },
          {
            path: 'needs-bank-match',
            pathMatch: 'full',
            redirectTo: '/app/transactions/needs-bank-match',
          },
          {
            path: 'bank-accounts',
            loadComponent: () =>
              import('./pages/admin/admin-bank-accounts').then(
                (m) => m.AdminBankAccountsComponent,
              ),
            canActivate: [AccessGuard],
            data: { permissions: ['ADMIN.BANK_ACCOUNTS.VIEW'] },
          },
          {
            // Billing moved out of Admin into its own top-level tab (see 'billing' below). Kept as
            // a redirect for old bookmarks/links.
            path: 'subscription',
            pathMatch: 'full',
            redirectTo: '/app/billing',
          },
          {
            path: 'cash-flow-forecast',
            pathMatch: 'full',
            redirectTo: '/app/reports/cash-flow-forecast',
          },
          {
            path: 'financial-reports',
            pathMatch: 'full',
            redirectTo: '/app/reports/financial-reports',
          },
          {
            path: 'roles',
            loadComponent: () =>
              import('./pages/admin/admin-roles').then((m) => m.AdminRolesComponent),
            canActivate: [AccessGuard],
            data: { permissions: ['ADMIN.ROLES.VIEW'] },
          },
          {
            path: 'components',
            loadComponent: () =>
              import('./pages/admin/admin-components').then((m) => m.AdminComponentsComponent),
            canActivate: [AccessGuard],
            data: { permissions: ['ADMIN.COMPONENTS.VIEW'] },
          },
          {
            path: 'permissions',
            component: AdminPermissionsComponent,
            canActivate: [AccessGuard],
            data: { permissions: ['ADMIN.PERMISSIONS.VIEW'] },
          },
          {
            path: 'users',
            loadComponent: () =>
              import('./pages/admin/admin-users').then((m) => m.AdminUsersComponent),
            canActivate: [AccessGuard],
            data: { permissions: ['ADMIN.USERS.VIEW'] },
          },
          {
            path: 'import-architecture',
            loadComponent: () =>
              import('./pages/admin/admin-import-architecture').then(
                (m) => m.AdminImportArchitectureComponent,
              ),
            canActivate: [AccessGuard],
            data: { permissions: ['ADMIN.IMPORT_ARCHITECTURE.VIEW'] },
          },
          {
            path: 'import-history',
            loadComponent: () =>
              import('./pages/admin/admin-import-history').then(
                (m) => m.AdminImportHistoryComponent,
              ),
            canActivate: [AccessGuard],
            data: { permissions: ['ADMIN.IMPORT_ARCHITECTURE.VIEW'] },
          },
          {
            path: 'audit-logs',
            loadComponent: () =>
              import('./pages/admin/admin-tenant-audit-logs').then(
                (m) => m.AdminTenantAuditLogsComponent,
              ),
            canActivate: [AccessGuard],
            data: { permissions: ['ADMIN.USERS.VIEW'] },
          },
        ],
      },
      {
        // Reporting used to live nested under Admin, which meant opening any report also
        // highlighted the Admin tab and showed Admin's unrelated sub-nav. It's now a top-level
        // module with its own sub-nav, same shape as Matcher/Imports.
        path: 'reports',
        component: ReportsShellComponent,
        canActivate: [AccessGuard],
        data: {
          scope: 'tenant',
          anyPermissions: [
            'ADMIN.DASHBOARD.VIEW',
            'ADMIN.CASH_FLOW_FORECAST.VIEW',
            'ADMIN.FINANCIAL_REPORTS.VIEW',
            'ADMIN.REPORT_SCHEDULES.MANAGE',
          ],
        },
        children: [
          {
            path: '',
            loadComponent: () =>
              import('./pages/admin/admin-reports-hub').then((m) => m.AdminReportsHubComponent),
            canActivate: [AccessGuard],
            data: { permissions: ['ADMIN.DASHBOARD.VIEW'] },
          },
          {
            path: 'cash-flow-forecast',
            loadComponent: () =>
              import('./pages/admin/admin-cash-flow-forecast').then(
                (m) => m.AdminCashFlowForecastComponent,
              ),
            canActivate: [AccessGuard],
            data: { permissions: ['ADMIN.CASH_FLOW_FORECAST.VIEW'] },
          },
          {
            path: 'financial-reports',
            loadComponent: () =>
              import('./pages/admin/admin-financial-reports-shell').then(
                (m) => m.AdminFinancialReportsShellComponent,
              ),
            canActivate: [AccessGuard],
            data: { permissions: ['ADMIN.FINANCIAL_REPORTS.VIEW'] },
            children: [
              {
                path: 'general-ledger',
                loadComponent: () =>
                  import('./pages/admin/admin-general-ledger').then(
                    (m) => m.AdminGeneralLedgerComponent,
                  ),
              },
              {
                path: 'trial-balance',
                loadComponent: () =>
                  import('./pages/admin/admin-trial-balance').then(
                    (m) => m.AdminTrialBalanceComponent,
                  ),
              },
              {
                path: 'income-statement',
                loadComponent: () =>
                  import('./pages/admin/admin-income-statement').then(
                    (m) => m.AdminIncomeStatementComponent,
                  ),
              },
              {
                path: 'balance-sheet',
                loadComponent: () =>
                  import('./pages/admin/admin-balance-sheet').then(
                    (m) => m.AdminBalanceSheetComponent,
                  ),
              },
              { path: '', pathMatch: 'full', redirectTo: 'general-ledger' },
            ],
          },
          {
            path: 'report-schedules',
            loadComponent: () =>
              import('./pages/admin/admin-report-schedules').then(
                (m) => m.AdminReportSchedulesComponent,
              ),
            canActivate: [AccessGuard],
            data: { permissions: ['ADMIN.REPORT_SCHEDULES.MANAGE'] },
          },
        ],
      },
      {
        // Billing used to live nested under Admin for the same reason described above for
        // 'reports'; promoted to its own top-level tab so it no longer drags Admin's nav along.
        path: 'billing',
        loadComponent: () =>
          import('./pages/admin/admin-subscription').then((m) => m.AdminSubscriptionComponent),
        canActivate: [AccessGuard],
        data: { scope: 'tenant', permissions: ['ADMIN.SUBSCRIPTIONS.MANAGE'] },
      },
      {
        path: 'system',
        component: AdminShellComponent,
        canActivate: [AccessGuard],
        data: {
          scope: 'system',
          anyPermissions: [
            'ADMIN.TENANT_REGISTRATIONS.MANAGE',
            'ADMIN.TENANTS.MANAGE',
            'ADMIN.PLANS.MANAGE',
            'ADMIN.PAYMENT_ALERTS.VIEW',
          ],
        },
        children: [
          {
            path: 'tenant-registrations',
            loadComponent: () =>
              import('./pages/admin/admin-tenant-registrations').then(
                (m) => m.AdminTenantRegistrationsComponent,
              ),
            canActivate: [AccessGuard],
            data: { permissions: ['ADMIN.TENANT_REGISTRATIONS.MANAGE'] },
          },
          {
            path: 'tenants',
            loadComponent: () =>
              import('./pages/admin/admin-tenants').then((m) => m.AdminTenantsComponent),
            canActivate: [AccessGuard],
            data: { permissions: ['ADMIN.TENANTS.MANAGE'] },
          },
          {
            path: 'plans',
            loadComponent: () =>
              import('./pages/admin/admin-plans').then((m) => m.AdminPlansComponent),
            canActivate: [AccessGuard],
            data: { permissions: ['ADMIN.PLANS.MANAGE'] },
          },
          {
            path: 'payment-alerts',
            loadComponent: () =>
              import('./pages/admin/admin-payment-alerts').then((m) => m.AdminPaymentAlertsComponent),
            canActivate: [AccessGuard],
            data: { permissions: ['ADMIN.PAYMENT_ALERTS.VIEW'] },
          },
          {
            path: 'audit-logs',
            loadComponent: () =>
              import('./pages/admin/admin-audit-logs').then((m) => m.AdminAuditLogsComponent),
            canActivate: [AccessGuard],
            data: { roles: ['ADMIN'], permissions: ['ADMIN.TENANTS.MANAGE'] },
          },
        ],
      },
      {
        // Transactions workflow moved out of Admin into its own module.
        // Keeps Admin focused on configuration and Transactions on workflow.
        path: 'transactions',
        canActivate: [AccessGuard],
        data: { scope: 'tenant', permissions: ['ADMIN.TRANSACTIONS.VIEW'] },
        children: [
          {
            path: '',
            loadComponent: () =>
              import('./pages/admin/admin-transactions').then(
                (m) => m.AdminTransactionsComponent,
              ),
          },
          {
            path: 'journal-ready',
            loadComponent: () =>
              import('./pages/admin/admin-journal-ready').then(
                (m) => m.AdminJournalReadyComponent,
              ),
          },
          {
            path: 'needs-bank-match',
            loadComponent: () =>
              import('./pages/admin/admin-needs-bank-match').then(
                (m) => m.AdminNeedsBankMatchComponent,
              ),
          },
        ],
      },
      {
        path: 'matcher',
        component: MatcherShellComponent,
        canActivate: [AccessGuard],
        data: { scope: 'tenant', permissions: ['ADMIN.RECONCILIATION.VIEW'] },
        children: [
          {
            path: '',
            component: MatcherPageComponent,
          },
          {
            path: 'waiting',
            loadComponent: () =>
              import('./pages/matcher/matcher-waiting').then((m) => m.MatcherWaitingComponent),
          },
          {
            path: 'sales-verification',
            loadComponent: () =>
              import('./pages/matcher/matcher-sales-verification').then((m) => m.MatcherSalesVerificationComponent),
          },
          {
            path: 'events',
            loadComponent: () =>
              import('./pages/matcher/matcher-events').then((m) => m.MatcherEventsComponent),
          },
          {
            path: 'trends',
            loadComponent: () =>
              import('./pages/matcher/matcher-trends').then((m) => m.MatcherTrendsComponent),
          },
          { path: '**', redirectTo: '' },
        ],
      },
      {
        path: 'imports',
        component: ImportsShellComponent,
        canActivate: [AccessGuard],
        data: {
          scope: 'tenant',
          anyPermissions: ['ADMIN.IMPORT_WORKBENCH.VIEW', 'ADMIN.IMPORT_ARCHITECTURE.VIEW'],
        },
        children: [
          {
            path: 'workbench',
            loadComponent: () =>
              import('./pages/imports/imports-workbench').then((m) => m.ImportsWorkbenchComponent),
            canActivate: [AccessGuard],
            data: { permissions: ['ADMIN.IMPORT_WORKBENCH.VIEW'] },
          },
          {
            path: 'import-architecture',
            loadComponent: () =>
              import('./pages/admin/admin-import-architecture').then(
                (m) => m.AdminImportArchitectureComponent,
              ),
            canActivate: [AccessGuard],
            data: { permissions: ['ADMIN.IMPORT_ARCHITECTURE.VIEW'] },
          },
          {
            path: 'import-history',
            loadComponent: () =>
              import('./pages/admin/admin-import-history').then(
                (m) => m.AdminImportHistoryComponent,
              ),
            canActivate: [AccessGuard],
            data: { permissions: ['ADMIN.IMPORT_ARCHITECTURE.VIEW'] },
          },
          { path: '', pathMatch: 'full', redirectTo: 'workbench' },
        ],
      },
      {
        path: 'imports',
        loadComponent: () =>
          import('./pages/imports/imports-workbench').then((m) => m.ImportsWorkbenchComponent),
        canActivate: [AccessGuard],
        data: { scope: 'tenant' },
      },
      {
        path: 'profile',
        component: ProfileComponent,
      },
      { path: 'not-authorized', component: NotAuthorizedComponent },
      { path: '', pathMatch: 'full', redirectTo: 'dashboard' },
    ],
  },
];
