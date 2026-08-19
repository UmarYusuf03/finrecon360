import { Component, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatSnackBar, MatSnackBarModule } from '@angular/material/snack-bar';
import { RouterLink } from '@angular/router';
import { TranslateModule } from '@ngx-translate/core';

import { ReconciliationReportsService } from '../../../core/admin-rbac/reconciliation-reports.service';
import { ReconciliationTrendDay } from '../../../core/admin-rbac/models';
import { ExportFormat, ExportService } from '../../../core/services/export.service';

interface DayPoint {
  date: string;
  matched: number;
  confirmed: number;
  exceptions: number;
  unmatched: number;
}

interface ChartLine {
  path: string;
  maxValue: number;
}

const LEVEL_OPTIONS = ['Level1', 'Level2', 'Level3', 'Level4', 'Level6', 'Level7'];

@Component({
  selector: 'app-matcher-trends',
  standalone: true,
  imports: [
    CommonModule,
    FormsModule,
    MatButtonModule,
    MatIconModule,
    MatSnackBarModule,
    RouterLink,
    TranslateModule,
  ],
  templateUrl: './matcher-trends.html',
  styleUrls: ['./matcher-trends.scss'],
})
export class MatcherTrendsComponent implements OnInit {
  readonly lookbackOptions = [30, 60, 90];
  readonly levelOptions = LEVEL_OPTIONS;
  readonly chartWidth = 680;
  readonly chartHeight = 160;

  selectedLookback = 30;
  selectedLevel: string | null = null;

  days: DayPoint[] = [];
  loading = true;
  error: string | null = null;
  exporting = false;

  matchLine: ChartLine = { path: '', maxValue: 0 };
  confirmedLine: ChartLine = { path: '', maxValue: 0 };
  exceptionLine: ChartLine = { path: '', maxValue: 0 };
  unmatchedLine: ChartLine = { path: '', maxValue: 0 };

  constructor(
    private reportsService: ReconciliationReportsService,
    private exportService: ExportService,
    private snackBar: MatSnackBar,
  ) {}

  ngOnInit(): void {
    this.load();
  }

  onFiltersChanged(): void {
    this.load();
  }

  load(): void {
    this.loading = true;
    this.error = null;

    const { fromUtc, toUtc } = this.buildRange();
    this.reportsService.getTrend(fromUtc, toUtc, this.selectedLevel).subscribe({
      next: (report) => {
        this.days = this.buildDenseSeries(fromUtc, toUtc, report.days);
        this.buildCharts();
        this.loading = false;
      },
      error: () => {
        this.error = 'Unable to load the reconciliation trend right now.';
        this.loading = false;
      },
    });
  }

  exportTrend(format: ExportFormat): void {
    if (this.exporting) {
      return;
    }

    this.exporting = true;
    const { fromUtc, toUtc } = this.buildRange();
    this.reportsService.exportTrend(fromUtc, toUtc, this.selectedLevel, format).subscribe({
      next: (blob) => {
        this.exporting = false;
        this.exportService.downloadBlob(blob, this.exportService.buildFilename('reconciliation-trend', format));
      },
      error: (error: unknown) => {
        this.exporting = false;
        this.exportService.extractErrorMessage(error).then((message) => {
          this.snackBar.open(message, 'Close', { duration: 3500 });
        });
      },
    });
  }

  private buildRange(): { fromUtc: string; toUtc: string } {
    const to = new Date();
    const from = new Date();
    from.setDate(from.getDate() - (this.selectedLookback - 1));
    return {
      fromUtc: `${from.toISOString().slice(0, 10)}T00:00:00.000Z`,
      toUtc: `${to.toISOString().slice(0, 10)}T23:59:59.999Z`,
    };
  }

  // The API only returns rows for (day, level) combinations that had activity. Trend lines need
  // one point per calendar day regardless, so gaps are filled with zeros — otherwise the SVG path
  // silently skips days with no activity instead of showing them as a dip to zero.
  private buildDenseSeries(fromUtc: string, toUtc: string, apiDays: ReconciliationTrendDay[]): DayPoint[] {
    const byDate = new Map<string, DayPoint>();
    for (const row of apiDays) {
      const dateKey = row.snapshotDate.slice(0, 10);
      const existing = byDate.get(dateKey) ?? { date: dateKey, matched: 0, confirmed: 0, exceptions: 0, unmatched: 0 };
      existing.matched += row.matchedCount;
      existing.confirmed += row.confirmedCount;
      existing.exceptions += row.exceptionCount;
      existing.unmatched += row.unmatchedCount;
      byDate.set(dateKey, existing);
    }

    const result: DayPoint[] = [];
    const cursor = new Date(fromUtc.slice(0, 10));
    const end = new Date(toUtc.slice(0, 10));
    while (cursor <= end) {
      const dateKey = cursor.toISOString().slice(0, 10);
      result.push(byDate.get(dateKey) ?? { date: dateKey, matched: 0, confirmed: 0, exceptions: 0, unmatched: 0 });
      cursor.setDate(cursor.getDate() + 1);
    }

    return result;
  }

  private buildCharts(): void {
    this.matchLine = this.buildLine(this.days.map((d) => d.matched));
    this.confirmedLine = this.buildLine(this.days.map((d) => d.confirmed));
    this.exceptionLine = this.buildLine(this.days.map((d) => d.exceptions));
    this.unmatchedLine = this.buildLine(this.days.map((d) => d.unmatched));
  }

  private buildLine(values: number[]): ChartLine {
    const maxValue = Math.max(1, ...values);
    if (values.length === 0) {
      return { path: '', maxValue };
    }

    const xStep = values.length > 1 ? this.chartWidth / (values.length - 1) : this.chartWidth;
    const path = values
      .map((value, index) => {
        const x = index * xStep;
        const y = this.chartHeight - (value / maxValue) * this.chartHeight;
        return `${index === 0 ? 'M' : 'L'} ${x.toFixed(1)} ${y.toFixed(1)}`;
      })
      .join(' ');

    return { path, maxValue };
  }
}
