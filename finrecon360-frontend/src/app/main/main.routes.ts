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
import { NotAuthorizedComponent } from './pages/not-authorized/not-authorized';
import { ProfileComponent } from './pages/profile/profile';

export const mainRoutes: Routes = [
  {
    path: '',
    component: ShellComponent,
    canActivate: [AuthGuard],
    children: [
      { path: 'dashboard', component: DashboardComponent },
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
            'ADMIN.AUDIT_LOGS.VIEW',
          ],
        },
        children: [
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
            path: 'audit-logs',
            loadComponent: () =>
              import('./pages/admin/admin-tenant-audit-logs').then(
                (m) => m.AdminTenantAuditLogsComponent,
              ),
            canActivate: [AccessGuard],
            data: { permissions: ['ADMIN.AUDIT_LOGS.VIEW'] },
          },
        ],
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
            'ADMIN.ENFORCEMENT.MANAGE',
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
            path: 'enforcement',
            loadComponent: () =>
              import('./pages/admin/admin-enforcement').then((m) => m.AdminEnforcementComponent),
            canActivate: [AccessGuard],
            data: { permissions: ['ADMIN.ENFORCEMENT.MANAGE'] },
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
        component: MatcherPageComponent,
        canActivate: [AccessGuard],
        data: { permissions: ['MATCHER.VIEW'] },
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
        path: 'profile',
        component: ProfileComponent,
      },
      { path: 'not-authorized', component: NotAuthorizedComponent },
      { path: '', pathMatch: 'full', redirectTo: 'dashboard' },
    ],
  },
];
