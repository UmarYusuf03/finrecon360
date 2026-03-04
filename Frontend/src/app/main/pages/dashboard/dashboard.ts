import { Component, OnDestroy, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { MatCardModule } from '@angular/material/card';
import { MatIconModule } from '@angular/material/icon';
import { TranslateModule } from '@ngx-translate/core';
import { Subject, takeUntil } from 'rxjs';

import { AuthService } from '../../../core/auth/auth.service';
import { DashboardData } from '../../models/dashboard.models';
import { DashboardService } from '../../services/dashboard.service';

@Component({
  selector: 'app-dashboard',
  standalone: true,
  imports: [CommonModule, TranslateModule, MatCardModule, MatIconModule],
  templateUrl: './dashboard.html',
  styleUrls: ['./dashboard.scss'],
})
export class DashboardComponent implements OnInit, OnDestroy {
  data?: DashboardData;
  isAdmin = false;
  canViewMatcher = false;
  canViewBalancer = false;
  canViewTasks = false;
  canViewJournal = false;
  canViewAnalytics = false;

  private destroy$ = new Subject<void>();

  constructor(
    private dashboardService: DashboardService,
    private authService: AuthService
  ) {}

  ngOnInit(): void {
    this.dashboardService
      .getDashboardData()
      .pipe(takeUntil(this.destroy$))
      .subscribe((payload) => (this.data = payload));

    this.authService.currentUser$
      .pipe(takeUntil(this.destroy$))
      .subscribe((user) => {
        this.isAdmin = !!user?.roles.includes('ADMIN');
        const permissions = user?.permissions ?? [];
        this.canViewMatcher = permissions.includes('MATCHER.VIEW');
        this.canViewBalancer = permissions.includes('BALANCER.VIEW');
        this.canViewTasks = permissions.includes('TASKS.VIEW');
        this.canViewJournal = permissions.includes('JOURNAL.VIEW') || this.isAdmin;
        this.canViewAnalytics = permissions.includes('ANALYTICS.VIEW') || this.isAdmin;
      });
  }

  ngOnDestroy(): void {
    this.destroy$.next();
    this.destroy$.complete();
  }

  get matchedPercent(): number {
    if (!this.data) return 0;
    return Math.round((this.data.matcher.matched / this.data.matcher.totalTransactions) * 100);
  }

  get unmatchedPercent(): number {
    return 100 - this.matchedPercent;
  }

  get reconciledPercent(): number {
    if (!this.data) return 0;
    return Math.round(
      (this.data.balancer.reconciledAccounts / this.data.balancer.totalAccounts) * 100
    );
  }
}
