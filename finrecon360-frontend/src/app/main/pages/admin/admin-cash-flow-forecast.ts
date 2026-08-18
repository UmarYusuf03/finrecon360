import { Component, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { TranslateModule } from '@ngx-translate/core';

import { CashFlowForecastService } from '../../../core/admin-rbac/cash-flow-forecast.service';
import { BankAccountService } from '../../../core/admin-rbac/bank-account.service';
import { BankAccount, CashFlowForecast } from '../../../core/admin-rbac/models';

interface ChartPoint {
  x: number;
  y: number;
  cumulative: number;
  date: string;
}

@Component({
  selector: 'app-admin-cash-flow-forecast',
  standalone: true,
  imports: [CommonModule, FormsModule, TranslateModule],
  templateUrl: './admin-cash-flow-forecast.html',
  styleUrls: ['./admin-cash-flow-forecast.scss'],
})
export class AdminCashFlowForecastComponent implements OnInit {
  readonly horizonOptions = [30, 60, 90];
  readonly chartWidth = 720;
  readonly chartHeight = 220;

  bankAccounts: BankAccount[] = [];
  selectedBankAccountId: string | null = null;
  selectedHorizon = 30;

  forecast: CashFlowForecast | null = null;
  loading = true;
  error: string | null = null;

  historyPath = '';
  forecastPath = '';
  zeroLineY = 0;
  todayX = 0;
  minY = 0;
  maxY = 0;

  constructor(
    private forecastService: CashFlowForecastService,
    private bankAccountService: BankAccountService,
  ) {}

  ngOnInit(): void {
    this.bankAccountService.getAll().subscribe((accounts) => {
      this.bankAccounts = accounts.filter((a) => a.isActive);
    });

    this.load();
  }

  onFiltersChanged(): void {
    this.load();
  }

  get projectedNetChange(): number {
    if (!this.forecast || this.forecast.forecast.length === 0) {
      return 0;
    }

    return this.forecast.forecast[this.forecast.forecast.length - 1].cumulativeNetFlow;
  }

  get trendDirection(): 'up' | 'down' | 'flat' {
    if (!this.forecast) return 'flat';
    if (this.forecast.dailyAverageNetFlow > 1) return 'up';
    if (this.forecast.dailyAverageNetFlow < -1) return 'down';
    return 'flat';
  }

  private load(): void {
    this.loading = true;
    this.error = null;

    this.forecastService.getForecast(this.selectedBankAccountId, this.selectedHorizon).subscribe({
      next: (forecast) => {
        this.forecast = forecast;
        this.buildChart(forecast);
        this.loading = false;
      },
      error: () => {
        this.error = 'Unable to load the cash flow forecast right now.';
        this.loading = false;
      },
    });
  }

  private buildChart(forecast: CashFlowForecast): void {
    const historyPoints: { date: string; cumulative: number }[] = [];
    let runningCumulative = 0;
    for (const day of forecast.history) {
      runningCumulative += day.netAmount;
      historyPoints.push({ date: day.date, cumulative: runningCumulative });
    }

    const forecastPoints = forecast.forecast.map((day) => ({
      date: day.date,
      cumulative: day.cumulativeNetFlow,
    }));

    const allValues = [0, ...historyPoints.map((p) => p.cumulative), ...forecastPoints.map((p) => p.cumulative)];
    const rawMin = Math.min(...allValues);
    const rawMax = Math.max(...allValues);
    const padding = Math.max((rawMax - rawMin) * 0.1, 1);
    this.minY = rawMin - padding;
    this.maxY = rawMax + padding;

    const totalPoints = historyPoints.length + forecastPoints.length;
    const xStep = totalPoints > 1 ? this.chartWidth / (totalPoints - 1) : this.chartWidth;

    const toChartPoint = (cumulative: number, index: number, date: string): ChartPoint => ({
      x: index * xStep,
      y: this.chartHeight - ((cumulative - this.minY) / (this.maxY - this.minY)) * this.chartHeight,
      cumulative,
      date,
    });

    const historyChartPoints = historyPoints.map((p, i) => toChartPoint(p.cumulative, i, p.date));
    const forecastChartPoints = forecastPoints.map((p, i) =>
      toChartPoint(p.cumulative, historyPoints.length - 1 + i + 1, p.date),
    );

    this.historyPath = this.toSvgPath(historyChartPoints);
    // The forecast line continues visually from the last historical point.
    this.forecastPath = this.toSvgPath(
      historyChartPoints.length > 0 ? [historyChartPoints[historyChartPoints.length - 1], ...forecastChartPoints] : forecastChartPoints,
    );

    this.zeroLineY = this.chartHeight - ((0 - this.minY) / (this.maxY - this.minY)) * this.chartHeight;
    this.todayX = (historyPoints.length - 1) * xStep;
  }

  private toSvgPath(points: ChartPoint[]): string {
    if (points.length === 0) {
      return '';
    }

    return points.map((p, i) => `${i === 0 ? 'M' : 'L'} ${p.x.toFixed(1)} ${p.y.toFixed(1)}`).join(' ');
  }
}
