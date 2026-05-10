import { Component, Input } from '@angular/core';
import { CommonModule } from '@angular/common';
import { MatCardModule } from '@angular/material/card';

import { ReportSnapshotDto } from '../../../../core/admin-rbac/reconciliation-kpi.service';

@Component({
  selector: 'app-reconciliation-trend-chart',
  standalone: true,
  imports: [CommonModule, MatCardModule],
  templateUrl: './reconciliation-trend-chart.html',
  styleUrls: ['./reconciliation-trend-chart.scss'],
})
export class ReconciliationTrendChartComponent {
  @Input() history: ReportSnapshotDto[] = [];

  /**
   * Returns the bar height as a CSS percentage string.
   * Designed so a Chart.js/Ng2-Charts implementation can replace this
   * by swapping the template and removing this method.
   */
  getBarHeight(percentage: number): string {
    return `${Math.max(percentage, 2)}%`; // min 2% so zero values are still visible
  }

  formatDate(dateStr: string): string {
    const d = new Date(dateStr);
    return d.toLocaleDateString('en-US', { month: 'short', day: 'numeric' });
  }
}
