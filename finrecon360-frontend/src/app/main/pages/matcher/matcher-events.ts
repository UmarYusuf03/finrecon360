import { Component, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { HttpErrorResponse } from '@angular/common/http';
import { MatButtonModule } from '@angular/material/button';
import { MatChipsModule } from '@angular/material/chips';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatSnackBar, MatSnackBarModule } from '@angular/material/snack-bar';
import { MatTooltipModule } from '@angular/material/tooltip';
import { MatSelectModule } from '@angular/material/select';
import { MatExpansionModule } from '@angular/material/expansion';
import { RouterLink } from '@angular/router';

import { ReconciliationService } from '../../../core/admin-rbac/reconciliation.service';
import { ReconciliationEvent } from '../../../core/admin-rbac/models';

/**
 * WHY: The Reconciliation Events panel gives auditors a complete per-batch timeline
 * of what the reconciliation engine did — which records were matched at Level 3 and 4,
 * which produced exceptions, and which are waiting for a settlement ID.
 * This replaces the audit-log deep-dive that would otherwise be needed to understand
 * the outcome of each import commit.
 *
 * Data source: GET /api/admin/reconciliation/events (all stages, filterable)
 */
@Component({
  selector: 'app-matcher-events',
  standalone: true,
  imports: [
    CommonModule,
    FormsModule,
    MatButtonModule,
    MatIconModule,
    MatChipsModule,
    MatSnackBarModule,
    MatProgressSpinnerModule,
    MatTooltipModule,
    MatSelectModule,
    MatExpansionModule,
    RouterLink,
  ],
  templateUrl: './matcher-events.html',
  styleUrls: ['./matcher-events.scss'],
})
export class MatcherEventsComponent implements OnInit {
  allEvents: ReconciliationEvent[] = [];
  filteredEvents: ReconciliationEvent[] = [];

  // Grouped by importBatchId for timeline view
  batches: { batchId: string; events: ReconciliationEvent[] }[] = [];

  loading = false;

  // Filters
  stageFilter = '';
  sourceTypeFilter = '';
  statusFilter = '';

  // Counts
  get level3Count(): number { return this.allEvents.filter(e => e.stage === 'Level3').length; }
  get level4Count(): number { return this.allEvents.filter(e => e.stage === 'Level4').length; }
  get exceptionCount(): number { return this.allEvents.filter(e => e.eventType === 'MatchNotFound' || e.eventType === 'AmountMismatch').length; }
  get matchedCount(): number { return this.allEvents.filter(e => e.eventType === 'MatchFound').length; }
  get pendingCount(): number { return this.allEvents.filter(e => e.status === 'Pending').length; }

  constructor(
    private reconciliationService: ReconciliationService,
    private snackBar: MatSnackBar,
  ) {}

  ngOnInit(): void {
    this.loadEvents();
  }

  refresh(): void {
    if (this.loading) return;
    this.loadEvents();
  }

  applyFilters(): void {
    this.filteredEvents = this.allEvents.filter(e => {
      const stageMatch = !this.stageFilter || e.stage === this.stageFilter;
      const sourceMatch = !this.sourceTypeFilter || e.sourceType === this.sourceTypeFilter;
      const statusMatch = !this.statusFilter || e.status === this.statusFilter;
      return stageMatch && sourceMatch && statusMatch;
    });
    this.groupByBatch();
  }

  countStage(events: ReconciliationEvent[], stage: string): number {
    return events.filter(e => e.stage === stage).length;
  }

  groupByBatch(): void {
    const map = new Map<string, ReconciliationEvent[]>();
    for (const event of this.filteredEvents) {
      const id = event.importBatchId;
      if (!map.has(id)) map.set(id, []);
      map.get(id)!.push(event);
    }
    this.batches = [...map.entries()].map(([batchId, events]) => ({ batchId, events }));
  }

  truncateBatchId(id: string): string {
    return id.length > 18 ? id.slice(0, 8) + '…' + id.slice(-6) : id;
  }

  getEventTypeLabel(type: string): string {
    switch (type) {
      case 'MatchFound': return 'Match Found';
      case 'MatchNotFound': return 'No Match';
      case 'AmountMismatch': return 'Amount Mismatch';
      case 'Confirmed': return 'Confirmed';
      default: return type;
    }
  }

  getEventIcon(type: string): string {
    switch (type) {
      case 'MatchFound': return 'check_circle';
      case 'MatchNotFound': return 'cancel';
      case 'AmountMismatch': return 'warning';
      case 'Confirmed': return 'verified';
      default: return 'circle';
    }
  }

  getEventClass(type: string): string {
    switch (type) {
      case 'MatchFound': return 'event-matched';
      case 'Confirmed': return 'event-confirmed';
      case 'MatchNotFound': return 'event-exception';
      case 'AmountMismatch': return 'event-warning';
      default: return 'event-default';
    }
  }

  getStageClass(stage: string): string {
    if (stage === 'Level1') return 'badge-level1';
    return stage === 'Level3' ? 'badge-level3' : 'badge-level4';
  }

  parseDetail(detailJson?: string | null): Record<string, unknown> | null {
    if (!detailJson) return null;
    try { return JSON.parse(detailJson) as Record<string, unknown>; }
    catch { return null; }
  }

  getUniqueSourceTypes(): string[] {
    return [...new Set(this.allEvents.map(e => e.sourceType))];
  }

  private loadEvents(): void {
    this.loading = true;
    this.reconciliationService.getEvents().subscribe({
      next: (events) => {
        this.allEvents = events;
        this.applyFilters();
        this.loading = false;
      },
      error: (error: unknown) => {
        this.loading = false;
        this.snackBar.open(this.extractErrorMessage(error), 'Close', { duration: 3500 });
      },
    });
  }

  private extractErrorMessage(error: unknown): string {
    if (error instanceof HttpErrorResponse) {
      const body = error.error as { message?: string } | null;
      return body?.message ?? `Request failed with status ${error.status}.`;
    }
    return 'Request failed.';
  }
}
