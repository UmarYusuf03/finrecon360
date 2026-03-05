import { Component } from '@angular/core';
import { CommonModule } from '@angular/common';
import { MatTabsModule } from '@angular/material/tabs';
import { RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { TranslateModule } from '@ngx-translate/core';
import { Observable } from 'rxjs';
import { map } from 'rxjs/operators';

import { AuthService } from '../../../core/auth/auth.service';

type AdminLink = {
  path: string;
  label: string;
  permission: string;
};

@Component({
  selector: 'app-admin-shell',
  standalone: true,
  imports: [CommonModule, RouterOutlet, RouterLink, RouterLinkActive, MatTabsModule, TranslateModule],
  templateUrl: './admin-shell.html',
  styleUrls: ['./admin-shell.scss'],
})
export class AdminShellComponent {
  private readonly links: AdminLink[] = [
    { path: '/app/admin/roles', label: 'ADMIN.ROLES.TITLE', permission: 'ADMIN.ROLES.VIEW' },
    { path: '/app/admin/components', label: 'ADMIN.COMPONENTS.TITLE', permission: 'ADMIN.COMPONENTS.VIEW' },
    { path: '/app/admin/permissions', label: 'ADMIN.PERMISSIONS.TITLE', permission: 'ADMIN.PERMISSIONS.VIEW' },
    { path: '/app/admin/users', label: 'ADMIN.USERS.TITLE', permission: 'ADMIN.USERS.VIEW' },
    { path: '/app/admin/tenant-registrations', label: 'ADMIN.TENANT_REGISTRATIONS.TITLE', permission: 'ADMIN.TENANT_REGISTRATIONS.MANAGE' },
    { path: '/app/admin/tenants', label: 'ADMIN.TENANTS.TITLE', permission: 'ADMIN.TENANTS.MANAGE' },
    { path: '/app/admin/plans', label: 'ADMIN.PLANS.TITLE', permission: 'ADMIN.PLANS.MANAGE' },
    { path: '/app/admin/enforcement', label: 'ADMIN.ENFORCEMENT.TITLE', permission: 'ADMIN.ENFORCEMENT.MANAGE' },
  ];

  readonly visibleLinks$: Observable<AdminLink[]>;

  constructor(private authService: AuthService) {
    this.visibleLinks$ = this.authService.currentUser$.pipe(
      map((user) => {
        if (!user) {
          return [] as AdminLink[];
        }

        return this.links.filter((link) => this.hasPermission(user.permissions, link.permission));
      })
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
