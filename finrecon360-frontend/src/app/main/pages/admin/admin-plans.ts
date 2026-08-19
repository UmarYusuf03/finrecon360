import { Component, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { HttpErrorResponse } from '@angular/common/http';
import { FormBuilder, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { TranslateModule, TranslateService } from '@ngx-translate/core';

import { PlanService } from '../../../core/admin-tenant/plan.service';
import { PlanSummary } from '../../../core/admin-tenant/models';

@Component({
  selector: 'app-admin-plans',
  standalone: true,
  imports: [CommonModule, ReactiveFormsModule, TranslateModule],
  templateUrl: './admin-plans.html',
  styleUrls: ['./admin-plans.scss'],
})
export class AdminPlansComponent implements OnInit {
  plans: PlanSummary[] = [];
  form: FormGroup;
  editingId: string | null = null;
  deletingId: string | null = null;
  deleteError: string | null = null;

  constructor(private service: PlanService, private fb: FormBuilder, private translate: TranslateService) {
    this.form = this.fb.group({
      code: ['', Validators.required],
      name: ['', Validators.required],
      priceCents: [0, [Validators.required, Validators.min(0)]],
      currency: ['LKR', Validators.required],
      durationDays: [30, [Validators.required, Validators.min(1)]],
      maxUsers: [10, [Validators.required, Validators.min(1)]],
      maxAccounts: [1, [Validators.required, Validators.min(1)]],
    });
  }

  ngOnInit(): void {
    this.load();
  }

  load(): void {
    this.service.getPlans().subscribe((plans) => (this.plans = plans));
  }

  edit(plan: PlanSummary): void {
    this.editingId = plan.id;
    this.form.patchValue(plan);
  }

  reset(): void {
    this.editingId = null;
    this.form.reset({
      code: '',
      name: '',
      priceCents: 0,
      currency: 'LKR',
      durationDays: 30,
      maxUsers: 10,
      maxAccounts: 1,
    });
  }

  save(): void {
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }
    const payload = this.form.value as Omit<PlanSummary, 'id' | 'isActive'>;

    if (this.editingId) {
      this.service.updatePlan(this.editingId, payload).subscribe(() => {
        this.load();
        this.reset();
      });
      return;
    }

    this.service.createPlan(payload).subscribe(() => {
      this.load();
      this.reset();
    });
  }

  deactivate(plan: PlanSummary): void {
    this.service.deactivatePlan(plan.id).subscribe(() => this.load());
  }

  activate(plan: PlanSummary): void {
    this.service.activatePlan(plan.id).subscribe(() => this.load());
  }

  delete(plan: PlanSummary): void {
    if (this.deletingId) {
      return;
    }

    const confirmMessage = this.translate.instant('ADMIN.PLANS.DELETE_CONFIRM');
    if (!window.confirm(confirmMessage)) {
      return;
    }

    this.deleteError = null;
    this.deletingId = plan.id;
    this.service.deletePlan(plan.id).subscribe({
      next: () => {
        this.deletingId = null;
        this.load();
      },
      error: (error: unknown) => {
        this.deletingId = null;
        this.deleteError = error instanceof HttpErrorResponse && error.status === 409
          ? this.translate.instant('ADMIN.PLANS.DELETE_IN_USE')
          : this.translate.instant('COMMON.REQUEST_FAILED');
      },
    });
  }
}
