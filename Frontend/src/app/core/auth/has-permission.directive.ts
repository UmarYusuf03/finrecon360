import { Directive, Input, OnDestroy, OnInit, TemplateRef, ViewContainerRef } from '@angular/core';
import { Subject, takeUntil } from 'rxjs';

import { AuthService } from './auth.service';
import { CurrentUser, PermissionCode } from './models';

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
    return required.every((permission) => user.permissions.includes(permission));
  }
}
