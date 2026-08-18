import { Component, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormBuilder, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { HttpErrorResponse } from '@angular/common/http';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatSelectModule } from '@angular/material/select';
import { MatSlideToggleModule } from '@angular/material/slide-toggle';
import { MatSnackBar, MatSnackBarModule } from '@angular/material/snack-bar';
import { MatTableModule } from '@angular/material/table';
import { TranslateModule } from '@ngx-translate/core';

import { ReportScheduleService } from '../../../core/admin-rbac/report-schedule.service';
import { ReportSchedule } from '../../../core/admin-rbac/models';

const WEEKDAY_KEYS = [
  'REPORT_SCHEDULES.WEEKDAYS.SUNDAY',
  'REPORT_SCHEDULES.WEEKDAYS.MONDAY',
  'REPORT_SCHEDULES.WEEKDAYS.TUESDAY',
  'REPORT_SCHEDULES.WEEKDAYS.WEDNESDAY',
  'REPORT_SCHEDULES.WEEKDAYS.THURSDAY',
  'REPORT_SCHEDULES.WEEKDAYS.FRIDAY',
  'REPORT_SCHEDULES.WEEKDAYS.SATURDAY',
];

@Component({
  selector: 'app-admin-report-schedules',
  standalone: true,
  imports: [
    CommonModule,
    ReactiveFormsModule,
    MatButtonModule,
    MatCardModule,
    MatFormFieldModule,
    MatIconModule,
    MatInputModule,
    MatSelectModule,
    MatSlideToggleModule,
    MatSnackBarModule,
    MatTableModule,
    TranslateModule,
  ],
  templateUrl: './admin-report-schedules.html',
  styleUrls: ['./admin-report-schedules.scss'],
})
export class AdminReportSchedulesComponent implements OnInit {
  readonly reportTypes = ['TrialBalance', 'IncomeStatement', 'BalanceSheet', 'ReconciliationTrend'];
  readonly formats = ['csv', 'xlsx'];
  readonly weekdayKeys = WEEKDAY_KEYS;
  readonly displayedColumns = ['reportType', 'format', 'dayOfWeek', 'recipientEmail', 'nextRunAt', 'lastRunAt', 'active', 'actions'];

  schedules: ReportSchedule[] = [];
  form!: FormGroup;
  loading = false;
  saving = false;
  deletingId: string | null = null;

  constructor(
    private scheduleService: ReportScheduleService,
    private fb: FormBuilder,
    private snackBar: MatSnackBar,
  ) {}

  ngOnInit(): void {
    this.form = this.fb.group({
      reportType: ['TrialBalance', Validators.required],
      format: ['csv', Validators.required],
      dayOfWeek: [1, Validators.required],
      recipientEmail: ['', [Validators.required, Validators.email]],
    });

    this.load();
  }

  load(): void {
    this.loading = true;
    this.scheduleService.getAll().subscribe({
      next: (schedules) => {
        this.schedules = schedules;
        this.loading = false;
      },
      error: (error: unknown) => {
        this.loading = false;
        this.snackBar.open(this.extractErrorMessage(error), 'Close', { duration: 3500 });
      },
    });
  }

  weekdayLabelKey(dayOfWeek: number): string {
    return this.weekdayKeys[dayOfWeek] ?? '';
  }

  create(): void {
    if (this.saving) {
      return;
    }

    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }

    this.saving = true;
    this.scheduleService.create(this.form.getRawValue()).subscribe({
      next: () => {
        this.saving = false;
        this.form.reset({ reportType: 'TrialBalance', format: 'csv', dayOfWeek: 1, recipientEmail: '' });
        this.load();
        this.snackBar.open('Report schedule created.', 'Close', { duration: 2500 });
      },
      error: (error: unknown) => {
        this.saving = false;
        this.snackBar.open(this.extractErrorMessage(error), 'Close', { duration: 3500 });
      },
    });
  }

  toggleActive(schedule: ReportSchedule): void {
    const nextActive = !schedule.isActive;
    this.scheduleService.setActive(schedule.reportScheduleId, nextActive).subscribe({
      next: () => {
        schedule.isActive = nextActive;
      },
      error: (error: unknown) => {
        this.snackBar.open(this.extractErrorMessage(error), 'Close', { duration: 3500 });
      },
    });
  }

  remove(schedule: ReportSchedule): void {
    if (this.deletingId) {
      return;
    }

    this.deletingId = schedule.reportScheduleId;
    this.scheduleService.delete(schedule.reportScheduleId).subscribe({
      next: () => {
        this.deletingId = null;
        this.schedules = this.schedules.filter((s) => s.reportScheduleId !== schedule.reportScheduleId);
      },
      error: (error: unknown) => {
        this.deletingId = null;
        this.snackBar.open(this.extractErrorMessage(error), 'Close', { duration: 3500 });
      },
    });
  }

  private extractErrorMessage(error: unknown): string {
    if (error instanceof HttpErrorResponse) {
      const body = error.error as { message?: string } | null;
      return body?.message ?? `Request failed with status ${error.status}.`;
    }

    return 'Request failed. Please try again.';
  }
}
