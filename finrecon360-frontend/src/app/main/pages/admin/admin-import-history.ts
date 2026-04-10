import { CommonModule } from '@angular/common';
import { HttpErrorResponse } from '@angular/common/http';
import { Component, OnInit } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { MatCardModule } from '@angular/material/card';
import { MatSnackBar, MatSnackBarModule } from '@angular/material/snack-bar';
import { TranslateModule } from '@ngx-translate/core';

import { ImportsService } from '../../../core/imports/imports.service';
import { ImportHistoryItem } from '../../../core/imports/imports.models';

@Component({
  selector: 'app-admin-import-history',
  standalone: true,
  imports: [CommonModule, FormsModule, MatCardModule, MatSnackBarModule, TranslateModule],
  templateUrl: './admin-import-history.html',
  styleUrls: ['./admin-import-history.scss'],
})
export class AdminImportHistoryComponent implements OnInit {
  loading = false;
  history: ImportHistoryItem[] = [];
  search = '';
  statusFilter = '';

  constructor(
    private readonly importsService: ImportsService,
    private readonly snackBar: MatSnackBar,
  ) {}

  ngOnInit(): void {
    this.loadHistory();
  }

  loadHistory(): void {
    this.loading = true;
    this.importsService
      .getImportHistory({
        search: this.search || undefined,
        status: this.statusFilter || undefined,
        page: 1,
        pageSize: 200,
      })
      .subscribe({
        next: (res) => {
          this.loading = false;
          this.history = res.items;
        },
        error: (error: unknown) => {
          this.loading = false;
          this.snackBar.open(this.getErrorMessage(error), 'Close', { duration: 3500 });
        },
      });
  }

  clearFilters(): void {
    this.search = '';
    this.statusFilter = '';
    this.loadHistory();
  }

  private getErrorMessage(error: unknown): string {
    if (error instanceof HttpErrorResponse) {
      const body = error.error as { message?: string } | null;
      return body?.message ?? `Request failed with status ${error.status}.`;
    }

    return 'Unable to load import history.';
  }
}
