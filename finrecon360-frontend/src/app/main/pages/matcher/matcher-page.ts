import { Component, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { HttpErrorResponse } from '@angular/common/http';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatChipsModule } from '@angular/material/chips';
import { MatDialogModule } from '@angular/material/dialog';
import { MatDividerModule } from '@angular/material/divider';
import { MatExpansionModule } from '@angular/material/expansion';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatSelectModule } from '@angular/material/select';
import { MatSnackBar, MatSnackBarModule } from '@angular/material/snack-bar';
import { MatTableModule } from '@angular/material/table';
import { MatTabsModule } from '@angular/material/tabs';
import { MatTooltipModule } from '@angular/material/tooltip';
import { RouterLink, RouterLinkActive } from '@angular/router';

import { ReconciliationService } from '../../../core/admin-rbac/reconciliation.service';
import { AuthService } from '../../../core/auth/auth.service';
import { ReconciliationMatchGroup, ReconciliationMatchedRecord } from '../../../core/admin-rbac/models';

@Component({
  selector: 'app-matcher-page',
  standalone: true,
  imports: [
    CommonModule,
    FormsModule,
    MatCardModule,
    MatTableModule,
    MatButtonModule,
    MatIconModule,
    MatSnackBarModule,
    MatProgressSpinnerModule,
    MatExpansionModule,
    MatTabsModule,
    MatChipsModule,
    MatDividerModule,
    MatSelectModule,
    MatTooltipModule,
    MatDialogModule,
    RouterLink,
    RouterLinkActive,
  ],
  templateUrl: './matcher-page.html',
  styleUrls: ['./matcher-page.scss'],
})
export class MatcherPageComponent implements OnInit {
  // Tab state: 0 = Unconfirmed (Level 4), 1 = All, 2 = Confirmed & Posted
  selectedTab = 0;

  allGroups: ReconciliationMatchGroup[] = [];
  displayedGroups: ReconciliationMatchGroup[] = [];

  loading = false;
  confirmingId: string | null = null;

  readonly memberColumns = ['sourceType', 'referenceNumber', 'transactionDate', 'grossAmount', 'processingFee', 'netAmount', 'matchStatus'];

  constructor(
    private reconciliationService: ReconciliationService,
    private authService: AuthService,
    private snackBar: MatSnackBar,
  ) {}

  private has(code: string): boolean {
    return this.authService.currentUser?.permissions.includes(code) ?? false;
  }

  // VIEW: satisfiable by any mutating permission via the AliasMap (backend implication)
  get canViewRecon(): boolean {
    return this.has('ADMIN.RECONCILIATION.VIEW');
  }
  // CONFIRM: gated by its own code; MANAGE legacy grants also pass via backend AliasMap
  get canConfirmMatch(): boolean {
    return this.has('ADMIN.RECONCILIATION.CONFIRM')
        || this.has('ADMIN.RECONCILIATION.MANAGE');
  }
  // POST: journal posting — ADMIN only in default seed
  get canPostJournal(): boolean {
    return this.has('ADMIN.JOURNAL.POST')
        || this.has('ADMIN.JOURNAL.MANAGE');
  }

  get pendingCount(): number { return this.allGroups.filter(g => !g.isConfirmed).length; }
  get confirmedCount(): number { return this.allGroups.filter(g => g.isConfirmed).length; }
  get postedCount(): number { return this.allGroups.filter(g => g.isJournalPosted).length; }

  trackById(_: number, group: ReconciliationMatchGroup): string {
    return group.reconciliationMatchGroupId;
  }

  ngOnInit(): void {
    this.loadGroups();
  }

  refresh(): void {
    if (this.loading) return;
    this.loadGroups();
  }

  onTabChange(index: number): void {
    this.selectedTab = index;
    this.applyTabFilter();
  }

  applyTabFilter(): void {
    switch (this.selectedTab) {
      case 0:
        this.displayedGroups = this.allGroups.filter(g => !g.isConfirmed);
        break;
      case 1:
        this.displayedGroups = [...this.allGroups];
        break;
      case 2:
        this.displayedGroups = this.allGroups.filter(g => g.isConfirmed && g.isJournalPosted);
        break;
    }
  }

  confirm(group: ReconciliationMatchGroup): void {
    if (this.confirmingId || !this.canConfirmMatch) return;

    this.confirmingId = group.reconciliationMatchGroupId;
    this.reconciliationService.confirmMatchGroup(group.reconciliationMatchGroupId).subscribe({
      next: (updated) => {
        const idx = this.allGroups.findIndex(g => g.reconciliationMatchGroupId === updated.reconciliationMatchGroupId);
        if (idx !== -1) this.allGroups[idx] = updated;
        this.applyTabFilter();
        this.confirmingId = null;
        this.snackBar.open('Match group confirmed. It is now eligible for journal posting.', 'Close', { duration: 4000 });
      },
      error: (error: unknown) => {
        this.confirmingId = null;
        this.snackBar.open(this.extractErrorMessage(error), 'Close', { duration: 4000 });
      },
    });
  }

  getMatchLevelLabel(level: string): string {
    switch (level) {
      case 'Level1': return 'Level 1 — POS Operational Match';
      case 'Level3': return 'Level 3 — Sales Match';
      case 'Level4': return 'Level 4 — Bank / Settlement';
      default: return level;
    }
  }

  getMatchLevelClass(level: string): string {
    switch (level) {
      case 'Level1': return 'badge-level1';
      case 'Level3': return 'badge-level3';
      case 'Level4': return 'badge-level4';
      default: return 'badge-default';
    }
  }

  getMatchStatusClass(status: string): string {
    switch (status) {
      case 'MATCHED': return 'status-matched';
      case 'INTERNAL_VERIFIED': return 'status-verified';
      case 'SALES_VERIFIED': return 'status-verified';
      case 'EXCEPTION': return 'status-exception';
      case 'WAITING': return 'status-waiting';
      default: return 'status-pending';
    }
  }

  getMatchStatusLabel(status: string): string {
    switch (status) {
      case 'MATCHED': return 'Matched';
      case 'INTERNAL_VERIFIED': return 'Internal Verified';
      case 'SALES_VERIFIED': return 'Sales Verified';
      case 'EXCEPTION': return 'Exception';
      case 'WAITING': return 'Waiting';
      default: return 'Pending';
    }
  }

  getNetTotal(group: ReconciliationMatchGroup): number {
    return group.matchedRecords.reduce((sum, r) => sum + r.netAmount, 0);
  }

  getGrossTotal(group: ReconciliationMatchGroup): number {
    return group.matchedRecords.reduce((sum, r) => sum + (r.grossAmount ?? 0), 0);
  }

  getFeeTotal(group: ReconciliationMatchGroup): number {
    return group.matchedRecords.reduce((sum, r) => sum + (r.processingFee ?? 0), 0);
  }

  private loadGroups(): void {
    this.loading = true;
    this.reconciliationService.getMatchGroups().subscribe({
      next: (groups) => {
        this.allGroups = groups;
        this.applyTabFilter();
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
