import { CommonModule } from '@angular/common';
import { Component, OnInit } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatCheckboxModule } from '@angular/material/checkbox';
import { MatIconModule } from '@angular/material/icon';
import { MatListModule } from '@angular/material/list';
import { MatSnackBar, MatSnackBarModule } from '@angular/material/snack-bar';
import { RouterLink } from '@angular/router';
import { BehaviorSubject, finalize, Subject, takeUntil } from 'rxjs';

import { MatchGroup } from '../../../../core/reconciliation/reconciliation.models';
import { ReconciliationService } from '../../../../core/reconciliation/reconciliation.service';

@Component({
  selector: 'app-reconciliation-review',
  standalone: true,
  imports: [
    CommonModule,
    MatCardModule,
    MatListModule,
    MatCheckboxModule,
    MatButtonModule,
    MatIconModule,
    MatSnackBarModule,
    RouterLink,
  ],
  templateUrl: './reconciliation-review.component.html',
})
export class ReconciliationReviewComponent implements OnInit {
  private readonly destroy$ = new Subject<void>();

  readonly groups$ = new BehaviorSubject<MatchGroup[]>([]);
  readonly selected = new Set<string>();
  readonly isLoading$ = new BehaviorSubject<boolean>(false);

  get groups(): MatchGroup[] {
    return this.groups$.value;
  }

  get isLoading(): boolean {
    return this.isLoading$.value;
  }

  constructor(
    private reconciliationService: ReconciliationService,
    private snackBar: MatSnackBar,
  ) {}

  ngOnInit(): void {
    this.loadProposedGroups();
  }

  private loadProposedGroups(): void {
    this.isLoading$.next(true);
    this.reconciliationService
      .getProposedMatchGroups()
      .pipe(takeUntil(this.destroy$), finalize(() => this.isLoading$.next(false)))
      .subscribe({
        next: (groups) => this.groups$.next(groups || []),
        error: () =>
          this.snackBar.open('Unable to load proposed matches.', 'Close', { duration: 3000 }),
      });
  }

  toggleSelect(groupId: string, checked: boolean): void {
    if (checked) this.selected.add(groupId);
    else this.selected.delete(groupId);
  }

  toggleSelectAll(checked: boolean): void {
    if (checked) {
      this.groups$.value.forEach((g) => this.selected.add(g.id));
    } else {
      this.selected.clear();
    }
  }

  confirmSelected(): void {
    if (this.selected.size === 0) {
      this.snackBar.open('No matches selected.', 'Close', { duration: 2000 });
      return;
    }

    const ids = Array.from(this.selected);
    this.reconciliationService.confirmMatches(ids).subscribe({
      next: () => {
        this.snackBar.open('Selected matches confirmed.', 'Close', { duration: 3000 });
        this.selected.clear();
        this.loadProposedGroups();
      },
      error: () =>
        this.snackBar.open('Confirmation failed. Please try again.', 'Close', {
          duration: 3000,
        }),
    });
  }
}
