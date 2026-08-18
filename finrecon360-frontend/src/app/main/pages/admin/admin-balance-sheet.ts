import { Component, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { TranslateModule } from '@ngx-translate/core';

import { FinancialReportsService } from '../../../core/admin-rbac/financial-reports.service';
import { BalanceSheetReport } from '../../../core/admin-rbac/models';
import { ExportFormat, ExportService } from '../../../core/services/export.service';

@Component({
  selector: 'app-admin-balance-sheet',
  standalone: true,
  imports: [CommonModule, FormsModule, TranslateModule],
  templateUrl: './admin-balance-sheet.html',
  styleUrls: ['./admin-financial-reports.scss'],
})
export class AdminBalanceSheetComponent implements OnInit {
  asOfDate = new Date().toISOString().slice(0, 10);

  report: BalanceSheetReport | null = null;
  loading = true;
  error: string | null = null;
  exporting = false;

  constructor(
    private reportsService: FinancialReportsService,
    private exportService: ExportService,
  ) {}

  ngOnInit(): void {
    this.load();
  }

  onFiltersChanged(): void {
    this.load();
  }

  load(): void {
    if (!this.asOfDate) {
      return;
    }

    this.loading = true;
    this.error = null;
    this.reportsService.getBalanceSheet(this.toEndOfDayUtc(this.asOfDate)).subscribe({
      next: (report) => {
        this.report = report;
        this.loading = false;
      },
      error: () => {
        this.error = 'Unable to load the balance sheet right now.';
        this.loading = false;
      },
    });
  }

  exportReport(format: ExportFormat): void {
    if (this.exporting || !this.asOfDate) {
      return;
    }

    this.exporting = true;
    this.reportsService.exportBalanceSheet(this.toEndOfDayUtc(this.asOfDate), format).subscribe({
      next: (blob) => {
        this.exporting = false;
        this.exportService.downloadBlob(blob, this.exportService.buildFilename('balance-sheet', format));
      },
      error: (error: unknown) => {
        this.exporting = false;
        this.exportService.extractErrorMessage(error).then((message) => {
          this.error = message;
        });
      },
    });
  }

  private toEndOfDayUtc(dateOnly: string): string {
    return `${dateOnly}T23:59:59.999Z`;
  }
}
