import { Injectable } from '@angular/core';
import { ActivatedRouteSnapshot, CanActivate, Router, UrlTree } from '@angular/router';

import { AuthService } from './auth.service';
import { PermissionCode, RoleCode } from './models';

@Injectable({
  providedIn: 'root',
})
export class AccessGuard implements CanActivate {
  // Temporary local toggle:
  // false = do not block non-admin users when tenantStatus is Pending/Suspended during pre-Stripe testing.
  // Set to true to restore strict "deny until tenant is Active (paid)" behavior.
  private readonly enforceTenantActiveStatus = false;

  constructor(private authService: AuthService, private router: Router) {}

  canActivate(route: ActivatedRouteSnapshot): boolean | UrlTree {
    const user = this.authService.currentUser;
    if (!user) {
      return this.router.parseUrl('/auth/login');
    }

    if (user.status && ['Suspended', 'Banned'].includes(user.status)) {
      return this.router.parseUrl('/app/not-authorized');
    }

    const requiredRoles = route.data?.['roles'] as RoleCode[] | undefined;
    const requiredPermissions = route.data?.['permissions'] as PermissionCode[] | undefined;

    const isAdminArea =
      (requiredRoles && requiredRoles.includes('ADMIN')) ||
      (requiredPermissions && requiredPermissions.some((permission) => permission.startsWith('ADMIN.')));

    if (
      this.enforceTenantActiveStatus &&
      !isAdminArea &&
      user.tenantStatus &&
      user.tenantStatus !== 'Active'
    ) {
      return this.router.parseUrl('/app/not-authorized');
    }

    const hasRole =
      !requiredRoles || requiredRoles.some((role) => user.roles.includes(role));

    const hasPermissions =
      !requiredPermissions ||
      requiredPermissions.every((permission) => this.hasPermission(user.permissions, permission));

    if (hasRole && hasPermissions) {
      return true;
    }

    return this.router.parseUrl('/app/not-authorized');
  }

  private hasPermission(grantedPermissions: PermissionCode[], requiredPermission: PermissionCode): boolean {
    if (grantedPermissions.includes(requiredPermission)) {
      return true;
    }

    const separatorIndex = requiredPermission.lastIndexOf('.');
    if (separatorIndex <= 0) {
      return false;
    }

    const manageCode = `${requiredPermission.slice(0, separatorIndex)}.MANAGE` as PermissionCode;
    return grantedPermissions.includes(manageCode);
  }
}
