import { CommonModule } from '@angular/common';
import { Component, OnInit } from '@angular/core';
import { MatCardModule } from '@angular/material/card';
import { MatTableModule } from '@angular/material/table';
import { MatSnackBar, MatSnackBarModule } from '@angular/material/snack-bar';
import { BehaviorSubject, finalize, Subject, takeUntil } from 'rxjs';

import { BankStatementLine } from '../../../../core/reconciliation/reconciliation.models';
import { ReconciliationService } from '../../../../core/reconciliation/reconciliation.service';

@Component({
  selector: 'app-reconciliation-exceptions',
  standalone: true,
  imports: [CommonModule, MatCardModule, MatTableModule, MatSnackBarModule],
  templateUrl: './reconciliation-exceptions.component.html',
})
export class ReconciliationExceptionsComponent implements OnInit {
  private readonly destroy$ = new Subject<void>();

  readonly lines$ = new BehaviorSubject<BankStatementLine[]>([]);
  readonly isLoading$ = new BehaviorSubject<boolean>(false);

  displayedColumns = ['transactionDate', 'description', 'amount'];

  constructor(private reconciliationService: ReconciliationService, private snackBar: MatSnackBar) {}

  ngOnInit(): void {
    this.loadExceptions();
  }

  private loadExceptions(): void {
    this.isLoading$.next(true);
    this.reconciliationService
      .getExceptions()
      .pipe(takeUntil(this.destroy$), finalize(() => this.isLoading$.next(false)))
      .subscribe({
        next: (lines) => this.lines$.next(lines || []),
        error: () => this.snackBar.open('Unable to load exceptions.', 'Close', { duration: 3000 }),
      });
  }
}
