import { Component, OnDestroy, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { RouterLink } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { Subject, takeUntil, forkJoin, catchError, of } from 'rxjs';

import {
  ReconciliationKpiService,
  ReportSnapshotDto,
} from '../../../../core/admin-rbac/reconciliation-kpi.service';
import { KpiSummaryCardsComponent } from '../kpi-summary-cards/kpi-summary-cards';
import { ReconciliationTrendChartComponent } from '../reconciliation-trend-chart/reconciliation-trend-chart';

@Component({
  selector: 'app-reconciliation-kpi-dashboard',
  standalone: true,
  imports: [
    CommonModule,
    RouterLink,
    MatButtonModule,
    MatCardModule,
    MatIconModule,
    MatProgressSpinnerModule,
    KpiSummaryCardsComponent,
    ReconciliationTrendChartComponent,
  ],
  templateUrl: './reconciliation-kpi-dashboard.html',
  styleUrls: ['./reconciliation-kpi-dashboard.scss'],
})
export class ReconciliationKpiDashboardComponent implements OnInit, OnDestroy {
  latestSnapshot: ReportSnapshotDto | null = null;
  snapshotHistory: ReportSnapshotDto[] = [];
  isLoading = true;
  isDayZero = false;
  errorMessage: string | null = null;

  private destroy$ = new Subject<void>();

  constructor(private kpiService: ReconciliationKpiService) {}

  ngOnInit(): void {
    this.loadDashboardData();
  }

  ngOnDestroy(): void {
    this.destroy$.next();
    this.destroy$.complete();
  }

  refresh(): void {
    this.loadDashboardData();
  }

  private loadDashboardData(): void {
    this.isLoading = true;
    this.isDayZero = false;
    this.errorMessage = null;

    // Calculate the last 30 days for historical trend
    const endDate = new Date();
    const startDate = new Date();
    startDate.setDate(endDate.getDate() - 30);

    forkJoin({
      latest: this.kpiService.getLatestSnapshot().pipe(
        catchError((err) => {
          if (err?.status === 404) {
            return of(null);
          }
          throw err;
        }),
      ),
      history: this.kpiService.getSnapshotHistory(
        startDate.toISOString().split('T')[0],
        endDate.toISOString().split('T')[0],
      ).pipe(
        catchError(() => of([] as ReportSnapshotDto[])),
      ),
    })
      .pipe(takeUntil(this.destroy$))
      .subscribe({
        next: ({ latest, history }) => {
          if (latest === null && history.length === 0) {
            this.isDayZero = true;
          } else {
            this.latestSnapshot = latest;
            this.snapshotHistory = history;
          }
          this.isLoading = false;
        },
        error: () => {
          this.errorMessage = 'Failed to load dashboard data. Please try again.';
          this.isLoading = false;
        },
      });
  }
}

