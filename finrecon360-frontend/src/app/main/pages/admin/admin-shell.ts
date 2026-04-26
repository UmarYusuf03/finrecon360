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
import { map, take } from 'rxjs/operators';

import { AuthService } from '../../../core/auth/auth.service';

type AdminLink = {
  path: string;
  label: string;
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
      path: '/app/admin/transactions',
      label: 'Transactions',
      permission: 'ADMIN.TRANSACTIONS.VIEW',
      scope: 'tenant',
    },
    {
      path: '/app/admin/journal-ready',
      label: 'Journal Ready',
      permission: 'ADMIN.TRANSACTIONS.VIEW',
      scope: 'tenant',
    },
    {
      path: '/app/admin/needs-bank-match',
      label: 'Needs Bank Match',
      permission: 'ADMIN.TRANSACTIONS.VIEW',
      scope: 'tenant',
    },
    {
      path: '/app/admin/bank-accounts',
      label: 'Bank Accounts',
      permission: 'ADMIN.BANK_ACCOUNTS.VIEW',
      scope: 'tenant',
    },
    {
      path: '/app/admin/roles',
      label: 'ADMIN.ROLES.TITLE',
      permission: 'ADMIN.ROLES.VIEW',
      scope: 'tenant',
    },
    {
      path: '/app/admin/components',
      label: 'ADMIN.COMPONENTS.TITLE',
      permission: 'ADMIN.COMPONENTS.VIEW',
      scope: 'tenant',
    },
    {
      path: '/app/admin/permissions',
      label: 'ADMIN.PERMISSIONS.TITLE',
      permission: 'ADMIN.PERMISSIONS.VIEW',
      scope: 'tenant',
    },
    {
      path: '/app/admin/users',
      label: 'ADMIN.USERS.TITLE',
      permission: 'ADMIN.USERS.VIEW',
      scope: 'tenant',
    },
    {
      path: '/app/admin/audit-logs',
      label: 'ADMIN.TENANT_AUDIT_LOGS.TITLE',
      permission: 'ADMIN.AUDIT_LOGS.VIEW',
      scope: 'tenant',
    },
    {
      path: '/app/system/tenant-registrations',
      label: 'ADMIN.TENANT_REGISTRATIONS.TITLE',
      permission: 'ADMIN.TENANT_REGISTRATIONS.MANAGE',
      scope: 'system',
    },
    {
      path: '/app/system/tenants',
      label: 'ADMIN.TENANTS.TITLE',
      permission: 'ADMIN.TENANTS.MANAGE',
      scope: 'system',
    },
    {
      path: '/app/system/plans',
      label: 'ADMIN.PLANS.TITLE',
      permission: 'ADMIN.PLANS.MANAGE',
      scope: 'system',
    },
    {
      path: '/app/system/enforcement',
      label: 'ADMIN.ENFORCEMENT.TITLE',
      permission: 'ADMIN.ENFORCEMENT.MANAGE',
      scope: 'system',
    },
    {
      path: '/app/system/audit-logs',
      label: 'ADMIN.AUDIT_LOGS.TITLE',
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
    this.visibleLinks$.pipe(take(1)).subscribe((links) => {
      const onScopeRoot =
        this.router.url === `/app/${this.scope}` || this.router.url === `/app/${this.scope}/`;
      if (!onScopeRoot) {
        return;
      }

      if (links.length > 0) {
        this.router.navigateByUrl(links[0].path);
        return;
      }

      this.router.navigate(['/app/not-authorized'], { relativeTo: this.route.root });
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
