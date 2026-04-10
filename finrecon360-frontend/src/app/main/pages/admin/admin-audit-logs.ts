import { Component, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';

import { AuditLogService } from '../../../core/audit-logs/audit-log.service';
import { AuditLogItem } from '../../../core/audit-logs/models';

@Component({
  selector: 'app-admin-audit-logs',
  standalone: true,
  imports: [CommonModule, FormsModule],
  templateUrl: './admin-audit-logs.html',
  styleUrls: ['./admin-audit-logs.scss'],
})
export class AdminAuditLogsComponent implements OnInit {
  logs: AuditLogItem[] = [];
  loading = true;
  error = '';

  page = 1;
  pageSize = 25;
  totalCount = 0;

  action = '';
  entity = '';
  userId = '';
  search = '';
  fromLocal = '';
  toLocal = '';

  constructor(private auditLogService: AuditLogService) {}

  ngOnInit(): void {
    this.load();
  }

  load(): void {
    this.loading = true;
    this.error = '';

    this.auditLogService
      .getAuditLogs({
        page: this.page,
        pageSize: this.pageSize,
        action: this.action,
        entity: this.entity,
        userId: this.userId,
        search: this.search,
        fromUtc: this.toUtcIso(this.fromLocal),
        toUtc: this.toUtcIso(this.toLocal),
      })
      .subscribe({
        next: (result) => {
          this.logs = result.items;
          this.totalCount = result.totalCount;
          this.page = result.page;
          this.pageSize = result.pageSize;
          this.loading = false;
        },
        error: () => {
          this.logs = [];
          this.totalCount = 0;
          this.loading = false;
          this.error =
            'Unable to load audit logs. Confirm backend endpoint /api/system/audit-logs is available.';
        },
      });
  }

  applyFilters(): void {
    this.page = 1;
    this.load();
  }

  resetFilters(): void {
    this.action = '';
    this.entity = '';
    this.userId = '';
    this.search = '';
    this.fromLocal = '';
    this.toLocal = '';
    this.page = 1;
    this.load();
  }

  previousPage(): void {
    if (this.page <= 1) {
      return;
    }

    this.page -= 1;
    this.load();
  }

  nextPage(): void {
    if (!this.hasNextPage) {
      return;
    }

    this.page += 1;
    this.load();
  }

  get hasNextPage(): boolean {
    return this.page * this.pageSize < this.totalCount;
  }

  get startIndex(): number {
    if (this.totalCount === 0) {
      return 0;
    }

    return (this.page - 1) * this.pageSize + 1;
  }

  get endIndex(): number {
    return Math.min(this.page * this.pageSize, this.totalCount);
  }

  formatMetadata(metadata: string | null): string {
    if (!metadata) {
      return '-';
    }

    return metadata.length > 120 ? `${metadata.slice(0, 120)}...` : metadata;
  }

  private toUtcIso(localDateTime: string): string | undefined {
    if (!localDateTime) {
      return undefined;
    }

    const local = new Date(localDateTime);
    if (Number.isNaN(local.getTime())) {
      return undefined;
    }

    return local.toISOString();
  }
}
