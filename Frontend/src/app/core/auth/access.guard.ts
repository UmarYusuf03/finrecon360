import { Injectable } from '@angular/core';
import { ActivatedRouteSnapshot, CanActivate, Router, UrlTree } from '@angular/router';

import { AuthService } from './auth.service';
import { PermissionCode, RoleCode } from './models';

@Injectable({
  providedIn: 'root',
})
export class AccessGuard implements CanActivate {
  constructor(private authService: AuthService, private router: Router) {}

  canActivate(route: ActivatedRouteSnapshot): boolean | UrlTree {
    const user = this.authService.currentUser;
    if (!user) {
      return this.router.parseUrl('/auth/login');
    }

    const requiredRoles = route.data?.['roles'] as RoleCode[] | undefined;
    const requiredPermissions = route.data?.['permissions'] as PermissionCode[] | undefined;

    const hasRole =
      !requiredRoles || requiredRoles.some((role) => user.roles.includes(role));

    const hasPermissions =
      !requiredPermissions ||
      requiredPermissions.every((permission) => user.permissions.includes(permission));

    if (hasRole && hasPermissions) {
      return true;
    }

    return this.router.parseUrl('/app/not-authorized');
  }
}
