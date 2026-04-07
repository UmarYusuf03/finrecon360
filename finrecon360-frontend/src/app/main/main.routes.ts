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
            'ADMIN.ROLES.VIEW',
            'ADMIN.COMPONENTS.VIEW',
            'ADMIN.PERMISSIONS.VIEW',
            'ADMIN.USERS.VIEW',
          ],
        },
        children: [
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
              import('./pages/admin/admin-tenant-registrations').then((m) => m.AdminTenantRegistrationsComponent),
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
        ],
      },
      {
        path: 'matcher',
        component: MatcherPageComponent,
        canActivate: [AccessGuard],
        data: { permissions: ['MATCHER.VIEW'] },
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
