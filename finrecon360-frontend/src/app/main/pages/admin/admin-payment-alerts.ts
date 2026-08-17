import { Component, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { TranslateModule } from '@ngx-translate/core';

import { PaymentAlertService } from '../../../core/admin-tenant/payment-alert.service';
import { BillingSettings, PaymentAlert } from '../../../core/admin-tenant/models';

type AlertFilter = 'Open' | 'Acknowledged' | 'Resolved' | 'All';

@Component({
  selector: 'app-admin-payment-alerts',
  standalone: true,
  imports: [CommonModule, FormsModule, RouterLink, TranslateModule],
  templateUrl: './admin-payment-alerts.html',
  styleUrls: ['./admin-payment-alerts.scss'],
})
export class AdminPaymentAlertsComponent implements OnInit {
  alerts: PaymentAlert[] = [];
  loading = true;
  activeFilter: AlertFilter = 'Open';
  readonly filters: AlertFilter[] = ['Open', 'Acknowledged', 'Resolved', 'All'];

  settings: BillingSettings | null = null;
  thresholdInput = 7;
  savingThreshold = false;
  thresholdEditing = false;

  actionId: string | null = null;
  actionMessage: string | null = null;
  actionError: string | null = null;

  constructor(private service: PaymentAlertService) {}

  ngOnInit(): void {
    this.load();
    this.loadSettings();
  }

  setFilter(filter: AlertFilter): void {
    this.activeFilter = filter;
    this.load();
  }

  load(): void {
    this.loading = true;
    const status = this.activeFilter === 'All' ? undefined : this.activeFilter;
    this.service.getAlerts(status).subscribe({
      next: (alerts) => {
        this.alerts = alerts;
        this.loading = false;
      },
      error: () => {
        this.loading = false;
      },
    });
  }

  severity(daysOverdue: number): 'mild' | 'moderate' | 'severe' {
    if (daysOverdue >= 30) return 'severe';
    if (daysOverdue >= 14) return 'moderate';
    return 'mild';
  }

  acknowledge(alert: PaymentAlert): void {
    if (this.actionId) return;
    this.actionId = alert.id;
    this.actionError = null;
    this.service.acknowledge(alert.id).subscribe({
      next: () => {
        this.actionId = null;
        this.actionMessage = `Alert for ${alert.tenantName} acknowledged.`;
        this.load();
      },
      error: (error) => {
        this.actionId = null;
        this.actionMessage = null;
        this.actionError = this.extractError(error);
      },
    });
  }

  resolve(alert: PaymentAlert): void {
    if (this.actionId) return;
    this.actionId = alert.id;
    this.actionError = null;
    this.service.resolve(alert.id).subscribe({
      next: () => {
        this.actionId = null;
        this.actionMessage = `Alert for ${alert.tenantName} marked resolved.`;
        this.load();
      },
      error: (error) => {
        this.actionId = null;
        this.actionMessage = null;
        this.actionError = this.extractError(error);
      },
    });
  }

  openThresholdEditor(): void {
    this.thresholdInput = this.settings?.paymentOverdueSuspensionThresholdDays ?? 7;
    this.thresholdEditing = true;
  }

  cancelThresholdEdit(): void {
    this.thresholdEditing = false;
  }

  saveThreshold(): void {
    if (this.savingThreshold || this.thresholdInput < 1 || this.thresholdInput > 365) {
      return;
    }

    this.savingThreshold = true;
    this.service.updateBillingSettings(this.thresholdInput).subscribe({
      next: (settings) => {
        this.settings = settings;
        this.savingThreshold = false;
        this.thresholdEditing = false;
      },
      error: (error) => {
        this.savingThreshold = false;
        this.actionError = this.extractError(error);
      },
    });
  }

  private loadSettings(): void {
    this.service.getBillingSettings().subscribe((settings) => {
      this.settings = settings;
      this.thresholdInput = settings.paymentOverdueSuspensionThresholdDays;
    });
  }

  private extractError(error: unknown): string {
    const message = (error as { error?: { message?: string } })?.error?.message;
    return message ?? 'Request failed. Please try again.';
  }
}
