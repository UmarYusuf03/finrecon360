import { Component, inject, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { Router } from '@angular/router';
import { MatchGroupsService, UnmatchedQueueItem } from '../../services/match-groups.service';
import { finalize } from 'rxjs/operators';

@Component({
  selector: 'app-matcher-unmatched',
  standalone: true,
  imports: [CommonModule],
  template: `
    <div class="matcher-unmatched p-8 max-w-7xl mx-auto">
      <div class="flex justify-between items-center mb-8">
        <div>
          <button (click)="goBack()" class="btn btn-outline btn-sm mb-4">
            <i class="bi bi-arrow-left me-2"></i> Back to Dashboard
          </button>
          <h1 class="text-3xl font-bold text-slate-900 dark:text-white">Unmatched Items Queue</h1>
          <p class="text-slate-500 mt-1">Review imported records that failed to automatically match</p>
        </div>
      </div>

      <div *ngIf="loading" class="text-center p-12">
        <div class="spinner-border text-primary"></div>
      </div>

      <div *ngIf="error" class="alert alert-danger">{{ error }}</div>

      <div *ngIf="!loading && !error && queue.length === 0" class="text-center p-12 bg-slate-50 dark:bg-slate-800 rounded-xl">
        <i class="bi bi-check-circle text-4xl text-emerald-500 mb-4 block"></i>
        <h3 class="text-lg font-medium text-slate-900 dark:text-white">No Unmatched Items</h3>
        <p class="text-slate-500">All imported records have been matched or queued for review.</p>
      </div>

      <div class="bg-white dark:bg-slate-800 rounded-xl shadow-sm border border-slate-200 dark:border-slate-700 overflow-hidden" *ngIf="!loading && queue.length > 0">
        <table class="w-full text-left border-collapse">
          <thead>
            <tr class="bg-slate-50 dark:bg-slate-800/50 text-slate-500 text-sm uppercase tracking-wider border-b border-slate-200 dark:border-slate-700">
              <th class="p-4 font-semibold">Date</th>
              <th class="p-4 font-semibold">Source</th>
              <th class="p-4 font-semibold">Reference</th>
              <th class="p-4 font-semibold">Amount</th>
              <th class="p-4 font-semibold">Status / Reason</th>
            </tr>
          </thead>
          <tbody class="divide-y divide-slate-200 dark:divide-slate-700">
            <ng-container *ngFor="let item of queue">
              <tr class="hover:bg-slate-50 dark:hover:bg-slate-700/50 transition cursor-pointer" (click)="toggleRow(item.importedNormalizedRecordId)">
                <td class="p-4 font-mono text-sm">{{ item.transactionDate | date:'shortDate' }}</td>
                <td class="p-4">
                  <span class="badge bg-slate-100 text-slate-700 dark:bg-slate-700 dark:text-slate-200">{{ item.sourceType }}</span>
                </td>
                <td class="p-4 font-mono text-sm text-slate-600 dark:text-slate-400">{{ item.referenceNumber || 'N/A' }}</td>
                <td class="p-4 font-bold">{{ item.amount | currency }}</td>
                <td class="p-4">
                  <div class="flex items-center gap-2">
                    <i class="bi bi-exclamation-triangle-fill text-amber-500"></i>
                    <span class="text-sm font-medium text-slate-700 dark:text-slate-300">{{ item.unmatchedReason }}</span>
                    <i class="bi bi-chevron-down ms-auto text-slate-400 transition-transform" [class.rotate-180]="expandedRows.has(item.importedNormalizedRecordId)"></i>
                  </div>
                </td>
              </tr>
              <!-- Hint Details Row -->
              <tr *ngIf="expandedRows.has(item.importedNormalizedRecordId) && item.hint" class="bg-amber-50/50 dark:bg-amber-900/10">
                <td colspan="5" class="p-6">
                  <div class="flex gap-4 items-start">
                    <div class="text-amber-500 text-2xl mt-1"><i class="bi bi-lightbulb-fill"></i></div>
                    <div>
                      <h4 class="font-semibold text-slate-900 dark:text-amber-100 mb-1">Intelligent Hint</h4>
                      <p class="text-slate-700 dark:text-slate-300 mb-3">{{ item.hint.hintMessage }}</p>
                      
                      <div class="bg-white dark:bg-slate-800 p-4 rounded-lg border border-amber-200 dark:border-amber-700/30 inline-block" *ngIf="item.hint.relatedTransactionId">
                        <div class="text-sm font-semibold text-slate-500 mb-2 uppercase">Suggested Match</div>
                        <div class="flex justify-between gap-8 items-end">
                          <div>
                            <div class="text-slate-900 dark:text-white font-medium">{{ item.hint.relatedDescription || 'Pending Transaction' }}</div>
                            <div class="font-mono text-xs text-slate-500 mt-1">ID: {{ item.hint.relatedTransactionId }}</div>
                          </div>
                          <div class="font-bold text-lg text-slate-900 dark:text-white">{{ item.hint.relatedAmount | currency }}</div>
                        </div>
                      </div>
                    </div>
                  </div>
                </td>
              </tr>
            </ng-container>
          </tbody>
        </table>
      </div>
    </div>
  `
})
export class MatcherUnmatchedComponent implements OnInit {
  private matchGroupsService = inject(MatchGroupsService);
  private router = inject(Router);

  queue: UnmatchedQueueItem[] = [];
  loading = true;
  error: string | null = null;
  expandedRows = new Set<string>();

  ngOnInit(): void {
    this.loadQueue();
  }

  loadQueue(): void {
    this.loading = true;
    this.matchGroupsService.getUnmatched('Expense')
      .pipe(finalize(() => this.loading = false))
      .subscribe({
        next: (data) => this.queue = data,
        error: () => this.error = 'Failed to load unmatched queue'
      });
  }

  toggleRow(id: string): void {
    if (this.expandedRows.has(id)) {
      this.expandedRows.delete(id);
    } else {
      this.expandedRows.add(id);
    }
  }

  goBack(): void {
    this.router.navigate(['/app/matcher']);
  }
}
