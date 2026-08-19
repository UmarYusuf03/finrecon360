import { Component, OnDestroy, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
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
  imports: [CommonModule, FormsModule, TranslateModule, MatCardModule, MatIconModule],
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
  currentUserDisplayName = '';
  currentTenantName = '';

  timeFilter: 'all' | 'weekly' | 'monthly' | 'yearly' | 'custom' = 'all';
  customStartDate?: string;
  customEndDate?: string;

  private destroy$ = new Subject<void>();

  constructor(
    private dashboardService: DashboardService,
    private authService: AuthService
  ) {}

  ngOnInit(): void {
    this.loadDashboardData();

    this.authService.currentUser$
      .pipe(takeUntil(this.destroy$))
      .subscribe((user) => {
        this.isAdmin = !!user?.roles.includes('ADMIN');
        this.currentUserDisplayName = user?.displayName ?? '';
        this.currentTenantName = user?.tenantName ?? '';
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

  loadDashboardData(): void {
    let start: string | undefined;
    let end: string | undefined;

    const today = new Date();

    if (this.timeFilter === 'weekly') {
      const lastWeek = new Date(today);
      lastWeek.setDate(today.getDate() - 7);
      start = lastWeek.toISOString();
    } else if (this.timeFilter === 'monthly') {
      const lastMonth = new Date(today);
      lastMonth.setMonth(today.getMonth() - 1);
      start = lastMonth.toISOString();
    } else if (this.timeFilter === 'yearly') {
      const lastYear = new Date(today);
      lastYear.setFullYear(today.getFullYear() - 1);
      start = lastYear.toISOString();
    } else if (this.timeFilter === 'custom') {
      if (this.customStartDate) {
        start = new Date(this.customStartDate).toISOString();
      }
      if (this.customEndDate) {
        end = new Date(this.customEndDate).toISOString();
      }
    }

    this.dashboardService
      .getDashboardData(start, end)
      .pipe(takeUntil(this.destroy$))
      .subscribe((payload) => (this.data = payload));
  }

  onFilterChange(filter: 'all' | 'weekly' | 'monthly' | 'yearly' | 'custom'): void {
    this.timeFilter = filter;
    if (filter !== 'custom') {
      this.loadDashboardData();
    }
  }

  applyCustomFilter(): void {
    if (this.timeFilter === 'custom') {
      this.loadDashboardData();
    }
  }

  /** Share of match groups that have been confirmed by a reviewer. */
  get matchConfirmationPercent(): number {
    if (!this.data || this.data.totalMatchGroups === 0) return 0;
    return Math.round((this.data.confirmedMatchGroups / this.data.totalMatchGroups) * 100);
  }

  /** Share of reconciliation events that ended in an exception/variance. */
  get exceptionRatePercent(): number {
    if (!this.data || this.data.totalEvents === 0) return 0;
    return Math.round((this.data.exceptionEvents / this.data.totalEvents) * 100);
  }

  /** Share of events that resolved cleanly (inverse of the exception rate). */
  get cleanRatePercent(): number {
    return 100 - this.exceptionRatePercent;
  }

  /** Share of transaction volume that has cleared through to JournalReady. */
  get journalReadyPercent(): number {
    if (!this.data || this.data.totalTransactions === 0) return 0;
    return Math.round((this.data.journalReadyTransactions / this.data.totalTransactions) * 100);
  }
}
