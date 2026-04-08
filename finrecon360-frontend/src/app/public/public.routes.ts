import type { Routes } from '@angular/router';

export const publicRoutes: Routes = [
  {
    path: 'tenant-register',
    loadComponent: () => import('./pages/tenant-register/tenant-register').then((m) => m.TenantRegisterComponent),
  },
];
