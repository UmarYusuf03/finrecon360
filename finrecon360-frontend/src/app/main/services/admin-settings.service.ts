import { Injectable } from '@angular/core';
import { Observable, of } from 'rxjs';

import {
  AdminComponentResource,
  AdminPermissionMatrixRow,
  AdminRole,
  AdminUser,
} from '../models/admin.models';

@Injectable({
  providedIn: 'root',
})
export class AdminSettingsService {
  // These mocks keep the UI functional until the ASP.NET backend is wired.
  private roles: AdminRole[] = [
    {
      code: 'ADMIN',
      name: 'Administrator',
      description: 'Full control over platform settings',
      permissions: [
        'ADMIN.ROLES.MANAGE',
        'ADMIN.PERMISSIONS.MANAGE',
        'ADMIN.USERS.MANAGE',
        'MATCHER.MANAGE',
        'BALANCER.MANAGE',
      ],
    },
    {
      code: 'ACCOUNTANT',
      name: 'Accountant',
      description: 'Works on matching and reconciliation',
      permissions: ['MATCHER.VIEW', 'BALANCER.VIEW', 'TASKS.VIEW'],
    },
  ];

  private components: AdminComponentResource[] = [
    { code: 'MATCHER', name: 'Matcher', actions: ['VIEW', 'MANAGE'] },
    { code: 'BALANCER', name: 'Balancer', actions: ['VIEW', 'MANAGE'] },
    { code: 'TASKS', name: 'Task Manager', actions: ['VIEW', 'APPROVE'] },
    { code: 'JOURNAL', name: 'Journal Entry', actions: ['VIEW', 'APPROVE'] },
  ];

  private users: AdminUser[] = [
    {
      id: 'u-1',
      name: 'Alex Accountant',
      email: 'alex@finrecon.local',
      roles: ['ACCOUNTANT'],
      status: 'active',
    },
    {
      id: 'u-2',
      name: 'Avery Admin',
      email: 'admin@finrecon.local',
      roles: ['ADMIN'],
      status: 'active',
    },
  ];

  getRoles(): Observable<AdminRole[]> {
    return of(this.roles);
  }

  getComponents(): Observable<AdminComponentResource[]> {
    return of(this.components);
  }

  getPermissionMatrix(): Observable<AdminPermissionMatrixRow[]> {
    const rows: AdminPermissionMatrixRow[] = [];
    this.roles.forEach((role) => {
      this.components.forEach((component) => {
        const actions = component.actions.filter((action) =>
          role.permissions.includes(`${component.code}.${action}`)
        );
        rows.push({
          role: role.name,
          component: component.name,
          actions,
        });
      });
    });
    return of(rows);
  }

  getUsers(): Observable<AdminUser[]> {
    return of(this.users);
  }
}
