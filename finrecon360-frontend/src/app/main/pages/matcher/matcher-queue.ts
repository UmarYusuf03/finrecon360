import { Component, inject, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { ActivatedRoute, Router } from '@angular/router';
import { MatchGroupsService, PendingMatchSummary } from '../../services/match-groups.service';
import { finalize } from 'rxjs/operators';

@Component({
  selector: 'app-matcher-queue',
  standalone: true,
  imports: [CommonModule],
  template: `
    <div class="matcher-queue p-8 max-w-7xl mx-auto">
      <div class="flex justify-between items-center mb-8">
        <div>
          <button (click)="goBack()" class="btn btn-outline btn-sm mb-4">
            <i class="bi bi-arrow-left me-2"></i> Back to Dashboard
          </button>
          <h1 class="text-3xl font-bold text-slate-900 dark:text-white">Confirmation Queue</h1>
          <p class="text-slate-500 mt-1">Review and manually confirm matches for {{ level || 'All Rules' }}</p>
        </div>
      </div>

      <div *ngIf="loading" class="text-center p-12">
        <div class="spinner-border text-primary"></div>
      </div>

      <div *ngIf="error" class="alert alert-danger">{{ error }}</div>

      <div *ngIf="!loading && !error && queue.length === 0" class="text-center p-12 bg-slate-50 dark:bg-slate-800 rounded-xl">
        <i class="bi bi-check-circle text-4xl text-emerald-500 mb-4 block"></i>
        <h3 class="text-lg font-medium text-slate-900 dark:text-white">All caught up!</h3>
        <p class="text-slate-500">No pending matches require your attention right now.</p>
      </div>

      <div class="space-y-6" *ngIf="!loading && queue.length > 0">
        <div *ngFor="let group of queue" class="bg-white dark:bg-slate-800 rounded-xl shadow-sm border border-slate-200 dark:border-slate-700 overflow-hidden">
          
          <div class="p-6 border-b border-slate-200 dark:border-slate-700 flex justify-between items-center bg-slate-50 dark:bg-slate-800/50">
            <div>
              <span class="badge bg-blue-100 text-blue-800 dark:bg-blue-900 dark:text-blue-200 me-2">{{ group.matchLevel }}</span>
              <span class="font-medium text-slate-700 dark:text-slate-300">{{ group.settlementKey }}</span>
            </div>
            <div class="flex items-center gap-3">
              <div class="text-right me-4">
                <div class="text-sm text-slate-500">Variance</div>
                <div class="font-bold" [ngClass]="group.variance > 0 ? 'text-rose-500' : 'text-emerald-500'">
                  {{ group.variance | currency }}
                </div>
              </div>
              <button class="btn btn-success text-white px-6" (click)="confirm(group.matchGroupId)">
                <i class="bi bi-check2-circle me-2"></i> Confirm
              </button>
              <button class="btn btn-outline-danger" (click)="reject(group.matchGroupId)">
                <i class="bi bi-x-circle me-2"></i> Reject
              </button>
            </div>
          </div>

          <div class="p-6">
            <h4 class="text-sm font-semibold text-slate-500 mb-4 uppercase tracking-wider">Matched Records</h4>
            <div class="grid md:grid-cols-2 gap-6">
              <!-- Source A side -->
              <div class="space-y-3">
                <div *ngFor="let r of getRecordsBySide(group, 'left')" class="p-4 rounded-lg bg-slate-50 dark:bg-slate-900 border border-slate-100 dark:border-slate-700">
                  <div class="flex justify-between mb-2">
                    <span class="font-bold text-slate-700 dark:text-slate-200">{{ r.sourceType }}</span>
                    <span class="font-mono text-slate-500">{{ r.transactionDate | date:'shortDate' }}</span>
                  </div>
                  <div class="flex justify-between items-end">
                    <div class="text-sm">
                      <div class="text-slate-500">{{ r.description || 'No description' }}</div>
                      <div class="font-mono text-xs text-slate-400 mt-1">Ref: {{ r.referenceNumber || 'N/A' }}</div>
                    </div>
                    <div class="text-lg font-bold text-slate-900 dark:text-white">{{ r.netAmount | currency }}</div>
                  </div>
                </div>
              </div>

              <!-- Source B side -->
              <div class="space-y-3">
                <div *ngFor="let r of getRecordsBySide(group, 'right')" class="p-4 rounded-lg bg-slate-50 dark:bg-slate-900 border border-slate-100 dark:border-slate-700">
                   <div class="flex justify-between mb-2">
                    <span class="font-bold text-slate-700 dark:text-slate-200">{{ r.sourceType }}</span>
                    <span class="font-mono text-slate-500">{{ r.transactionDate | date:'shortDate' }}</span>
                  </div>
                  <div class="flex justify-between items-end">
                    <div class="text-sm">
                      <div class="text-slate-500">{{ r.description || 'No description' }}</div>
                      <div class="font-mono text-xs text-slate-400 mt-1">Ref: {{ r.referenceNumber || 'N/A' }}</div>
                    </div>
                    <div class="text-lg font-bold text-slate-900 dark:text-white">{{ r.netAmount | currency }}</div>
                  </div>
                </div>
              </div>
            </div>
          </div>
        </div>
      </div>
    </div>
  `
})
export class MatcherQueueComponent implements OnInit {
  private matchGroupsService = inject(MatchGroupsService);
  private route = inject(ActivatedRoute);
  private router = inject(Router);

  level: string | null = null;
  queue: PendingMatchSummary[] = [];
  loading = true;
  error: string | null = null;

  ngOnInit(): void {
    this.route.queryParams.subscribe(params => {
      this.level = params['level'] || null;
      this.loadQueue();
    });
  }

  loadQueue(): void {
    this.loading = true;
    this.matchGroupsService.getPendingMatches(this.level || undefined)
      .pipe(finalize(() => this.loading = false))
      .subscribe({
        next: (data) => this.queue = data,
        error: () => this.error = 'Failed to load confirmation queue'
      });
  }

  getRecordsBySide(group: PendingMatchSummary, side: 'left' | 'right') {
    // Basic heuristic: first record is left, rest are right. 
    // Ideally we filter based on SourceType corresponding to the rule.
    if (side === 'left') {
      return group.records.length > 0 ? [group.records[0]] : [];
    } else {
      return group.records.slice(1);
    }
  }

  confirm(id: string): void {
    const note = prompt('Add optional confirmation note:');
    if (note !== null) {
      this.matchGroupsService.confirmMatch(id, note).subscribe({
        next: () => this.loadQueue(),
        error: (err) => alert('Failed to confirm match: ' + err.message)
      });
    }
  }

  reject(id: string): void {
    const reason = prompt('Please enter rejection reason:');
    if (reason) {
      this.matchGroupsService.rejectMatch(id, reason).subscribe({
        next: () => this.loadQueue(),
        error: (err) => alert('Failed to reject match: ' + err.message)
      });
    }
  }

  goBack(): void {
    this.router.navigate(['/app/matcher']);
  }
}
