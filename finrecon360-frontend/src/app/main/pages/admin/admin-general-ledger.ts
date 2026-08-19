import { Component, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { TranslateModule } from '@ngx-translate/core';

import { FinancialReportsService } from '../../../core/admin-rbac/financial-reports.service';
import { GeneralLedgerReport } from '../../../core/admin-rbac/models';
import { ExportFormat, ExportService } from '../../../core/services/export.service';

@Component({
  selector: 'app-admin-general-ledger',
  standalone: true,
  imports: [CommonModule, FormsModule, TranslateModule],
  templateUrl: './admin-general-ledger.html',
  styleUrls: ['./admin-financial-reports.scss'],
})
export class AdminGeneralLedgerComponent implements OnInit {
  fromDate = this.toDateInputValue(this.daysAgo(30));
  toDate = this.toDateInputValue(new Date());

  report: GeneralLedgerReport | null = null;
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
    if (!this.fromDate || !this.toDate) {
      return;
    }

    this.loading = true;
    this.error = null;
    this.reportsService.getGeneralLedger(this.toStartOfDayUtc(this.fromDate), this.toEndOfDayUtc(this.toDate)).subscribe({
      next: (report) => {
        this.report = report;
        this.loading = false;
      },
      error: () => {
        this.error = 'Unable to load the general ledger right now.';
        this.loading = false;
      },
    });
  }

  exportReport(format: ExportFormat): void {
    if (this.exporting || !this.fromDate || !this.toDate) {
      return;
    }

    this.exporting = true;
    this.reportsService
      .exportGeneralLedger(this.toStartOfDayUtc(this.fromDate), this.toEndOfDayUtc(this.toDate), format)
      .subscribe({
        next: (blob) => {
          this.exporting = false;
          this.exportService.downloadBlob(blob, this.exportService.buildFilename('general-ledger', format));
        },
        error: (error: unknown) => {
          this.exporting = false;
          this.exportService.extractErrorMessage(error).then((message) => {
            this.error = message;
          });
        },
      });
  }

  private daysAgo(days: number): Date {
    const date = new Date();
    date.setDate(date.getDate() - days);
    return date;
  }

  private toDateInputValue(date: Date): string {
    return date.toISOString().slice(0, 10);
  }

  private toStartOfDayUtc(dateOnly: string): string {
    return `${dateOnly}T00:00:00.000Z`;
  }

  private toEndOfDayUtc(dateOnly: string): string {
    return `${dateOnly}T23:59:59.999Z`;
  }
}
