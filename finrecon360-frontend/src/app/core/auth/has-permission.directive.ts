import { Directive, Input, OnDestroy, OnInit, TemplateRef, ViewContainerRef } from '@angular/core';
import { Subject, takeUntil } from 'rxjs';

import { AuthService } from './auth.service';
import { CurrentUser, PermissionCode } from './models';

/**
 * WHY: This structural directive (`*appHasPermission`) is used instead of a simple CSS `display: none` 
 * toggle. By managing the `ViewContainerRef`, we ensure that restricted DOM trees, components, 
 * and their respective network calls are never evaluated or loaded into memory if the user lacks access.
 */
@Directive({
  selector: '[appHasPermission]',
  standalone: true,
})
export class HasPermissionDirective implements OnInit, OnDestroy {
  @Input('appHasPermission') required: PermissionCode | PermissionCode[] = [];

  private destroy$ = new Subject<void>();
  private isViewCreated = false;

  constructor(
    private templateRef: TemplateRef<unknown>,
    private viewContainer: ViewContainerRef,
    private authService: AuthService
  ) {}

  ngOnInit(): void {
    this.authService.currentUser$
      .pipe(takeUntil(this.destroy$))
      .subscribe((user) => this.updateView(user));
  }

  ngOnDestroy(): void {
    this.destroy$.next();
    this.destroy$.complete();
  }

  private updateView(user: CurrentUser | null): void {
    if (this.hasPermissions(user)) {
      if (!this.isViewCreated) {
        this.viewContainer.createEmbeddedView(this.templateRef);
        this.isViewCreated = true;
      }
    } else {
      this.viewContainer.clear();
      this.isViewCreated = false;
    }
  }

  private hasPermissions(user: CurrentUser | null): boolean {
    if (!user) return false;
    const required = Array.isArray(this.required) ? this.required : [this.required];
    if (!required.length) return true;
    return required.every((permission) => this.hasPermission(user.permissions, permission));
  }

  /**
   * WHY: Handles hierarchical permission fallbacks. If a user is checking for `ADMIN.ROLES.VIEW_LIST`,
   * but their role only grants `ADMIN.ROLES.MANAGE` (a wildcard/super permission for that module),
   * this logic extracts the prefix and safely evaluates the `MANAGE` fallback, ensuring 
   * high-level admins don't need exhausting 1:1 granular assignments.
   */
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
