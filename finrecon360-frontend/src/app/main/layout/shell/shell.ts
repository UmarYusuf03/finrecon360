import { Component, OnDestroy, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { MatMenuModule } from '@angular/material/menu';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatTooltipModule } from '@angular/material/tooltip';
import { Router, RouterLink, RouterOutlet, RouterLinkActive } from '@angular/router';
import { TranslateModule } from '@ngx-translate/core';
import { Observable, of, Subject, timer } from 'rxjs';
import { catchError, switchMap, takeUntil } from 'rxjs/operators';

import { AuthService } from '../../../core/auth/auth.service';
import { ProfileService } from '../../services/profile.service';
import { PaymentAlertService } from '../../../core/admin-tenant/payment-alert.service';
import { SubscriptionService } from '../../../core/admin-tenant/subscription.service';
import { CurrentUser } from '../../../core/auth/models';
import { LanguageSwitcherComponent } from '../../../shared/components/language-switcher/language-switcher';

@Component({
  selector: 'app-shell',
  standalone: true,
  imports: [
    CommonModule,
    RouterOutlet,
    RouterLink,
    RouterLinkActive,
    LanguageSwitcherComponent,
    TranslateModule,
    MatMenuModule,
    MatButtonModule,
    MatIconModule,
    MatTooltipModule,
  ],
  templateUrl: './shell.html',
  styleUrls: ['./shell.scss'],
})
export class ShellComponent implements OnInit, OnDestroy {
  readonly adminEntryPermissions: string[] = [
    'ADMIN.DASHBOARD.VIEW',
    'ADMIN.USERS.VIEW',
    'ADMIN.ROLES.VIEW',
    'ADMIN.COMPONENTS.VIEW',
    'ADMIN.PERMISSIONS.VIEW',
    'ADMIN.AUDIT_LOGS.VIEW',
    'ADMIN.BANK_ACCOUNTS.VIEW',
    'ADMIN.TRANSACTIONS.VIEW',
    'ADMIN.TENANT_REGISTRATIONS.MANAGE',
    'ADMIN.TENANTS.MANAGE',
    'ADMIN.PLANS.MANAGE',
    'ADMIN.ENFORCEMENT.MANAGE',
    'ADMIN.PAYMENT_ALERTS.VIEW',
  ];

  readonly importEntryPermissions: string[] = [
    'ADMIN.IMPORT_WORKBENCH.VIEW',
    'ADMIN.IMPORT_ARCHITECTURE.VIEW',
  ];

  readonly transactionEntryPermissions: string[] = ['ADMIN.TRANSACTIONS.VIEW'];

  user$: Observable<CurrentUser | null>;
  profileImageUrl: string | null = null;
  openPaymentAlertCount = 0;
  subscriptionDaysOverdue: number | null = null;

  private destroy$ = new Subject<void>();
  private static readonly PaymentAlertPollInterval = 60000;

  constructor(
    private authService: AuthService,
    private router: Router,
    private profileService: ProfileService,
    private paymentAlertService: PaymentAlertService,
    private subscriptionService: SubscriptionService,
  ) {
    // assign here so it is initialized after DI
    this.user$ = this.authService.currentUser$;
  }

  ngOnInit(): void {
    this.authService.currentUser$
      .pipe(takeUntil(this.destroy$))
      .subscribe((user) => {
        if (!user) {
          this.clearProfileImageUrl();
          return;
        }

        this.profileService.getProfile().pipe(takeUntil(this.destroy$)).subscribe((profile) => {
          if (profile.hasProfileImage) {
            this.loadProfileImage();
          } else {
            this.clearProfileImageUrl();
          }
        });

        if (user.isSystemAdmin && this.hasAnyPermission(user, ['ADMIN.PAYMENT_ALERTS.VIEW'])) {
          this.startPaymentAlertPolling();
        }

        // Only the tenant users who can actually do something about a lapsed subscription
        // (i.e. hold the permission to view/pay it) get checked for the overdue banner.
        if (!user.isSystemAdmin && this.hasAnyPermission(user, ['ADMIN.SUBSCRIPTIONS.MANAGE'])) {
          this.checkSubscriptionOverdue();
        }
      });
  }

  private startPaymentAlertPolling(): void {
    timer(0, ShellComponent.PaymentAlertPollInterval)
      .pipe(
        switchMap(() => this.paymentAlertService.getSummary().pipe(catchError(() => of({ openCount: 0 })))),
        takeUntil(this.destroy$),
      )
      .subscribe((summary) => (this.openPaymentAlertCount = summary.openCount));
  }

  private checkSubscriptionOverdue(): void {
    this.subscriptionService.getTenantSubscriptionOverview()
      .pipe(takeUntil(this.destroy$))
      .subscribe({
        next: (overview) => {
          const subscription = overview.currentSubscription;
          if (subscription?.status === 'PastDue' && subscription.periodEnd) {
            const periodEnd = new Date(subscription.periodEnd).getTime();
            const days = Math.floor((Date.now() - periodEnd) / (1000 * 60 * 60 * 24));
            this.subscriptionDaysOverdue = Math.max(days, 0);
          } else {
            this.subscriptionDaysOverdue = null;
          }
        },
        error: () => (this.subscriptionDaysOverdue = null),
      });
  }

  ngOnDestroy(): void {
    this.destroy$.next();
    this.destroy$.complete();
    this.clearProfileImageUrl();
  }

  getInitials(name: string | null | undefined): string {
    if (!name) return 'U';
    const parts = name.trim().split(/\s+/).filter(Boolean);
    if (!parts.length) return 'U';
    const first = parts[0]?.[0] ?? '';
    const second = parts.length > 1 ? parts[parts.length - 1]?.[0] ?? '' : '';
    return (first + second).toUpperCase();
  }

  logout(): void {
    this.authService.logout();
    this.router.navigateByUrl('/auth/login');
  }

  hasAnyPermission(user: CurrentUser | null, permissions: string[]): boolean {
    if (!user) {
      return false;
    }

    return permissions.some((permission) => user.permissions.includes(permission));
  }

  hasPermission(grantedPermissions: string[], requiredPermission: string): boolean {
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

  getAdminRoot(user: CurrentUser | null): string {
    return user?.isSystemAdmin ? '/app/system' : '/app/admin';
  }

  private loadProfileImage(): void {
    this.profileService.getProfileImage().subscribe({
      next: (blob) => {
        this.clearProfileImageUrl();
        this.profileImageUrl = URL.createObjectURL(blob);
      },
      error: () => {
        this.clearProfileImageUrl();
      },
    });
  }

  private clearProfileImageUrl(): void {
    if (this.profileImageUrl) {
      URL.revokeObjectURL(this.profileImageUrl);
      this.profileImageUrl = null;
    }
  }
}
