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
        data: { roles: ['ADMIN'], permissions: ['ADMIN.DASHBOARD.VIEW'] },
        children: [
          { path: '', pathMatch: 'full', redirectTo: 'roles' },
          {
            path: 'roles',
            loadComponent: () =>
              import('./pages/admin/admin-roles').then((m) => m.AdminRolesComponent),
            canActivate: [AccessGuard],
            data: { permissions: ['ADMIN.ROLES.MANAGE'] },
          },
          {
            path: 'components',
            loadComponent: () =>
              import('./pages/admin/admin-components').then((m) => m.AdminComponentsComponent),
            canActivate: [AccessGuard],
            data: { permissions: ['ADMIN.COMPONENTS.MANAGE'] },
          },
          {
            path: 'permissions',
            component: AdminPermissionsComponent,
            canActivate: [AccessGuard],
            data: { permissions: ['ADMIN.PERMISSIONS.MANAGE'] },
          },
          {
            path: 'users',
            loadComponent: () =>
              import('./pages/admin/admin-users').then((m) => m.AdminUsersComponent),
            canActivate: [AccessGuard],
            data: { permissions: ['ADMIN.USERS.MANAGE'] },
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
