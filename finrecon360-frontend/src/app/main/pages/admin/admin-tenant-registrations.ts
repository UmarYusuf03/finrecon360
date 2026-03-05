import { Component, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';

import { TenantRegistrationService } from '../../../core/admin-tenant/tenant-registration.service';
import { TenantRegistrationSummary } from '../../../core/admin-tenant/models';

@Component({
  selector: 'app-admin-tenant-registrations',
  standalone: true,
  imports: [CommonModule, FormsModule],
  templateUrl: './admin-tenant-registrations.html',
  styleUrls: ['./admin-tenant-registrations.scss'],
})
export class AdminTenantRegistrationsComponent implements OnInit {
  registrations: TenantRegistrationSummary[] = [];
  loading = true;
  statusFilter = 'PENDING_REVIEW';

  constructor(private service: TenantRegistrationService) {}

  ngOnInit(): void {
    this.load();
  }

  load(): void {
    this.loading = true;
    this.service.getRegistrations(this.statusFilter).subscribe((items) => {
      this.registrations = items;
      this.loading = false;
    });
  }

  approve(reg: TenantRegistrationSummary): void {
    const note = prompt('Approval note (optional):') ?? undefined;
    this.service.approve(reg.id, note).subscribe(() => this.load());
  }

  reject(reg: TenantRegistrationSummary): void {
    const note = prompt('Rejection note (optional):') ?? undefined;
    this.service.reject(reg.id, note).subscribe(() => this.load());
  }
}
