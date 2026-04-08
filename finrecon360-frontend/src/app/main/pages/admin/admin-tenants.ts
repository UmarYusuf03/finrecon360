import { Component, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';

import { TenantService } from '../../../core/admin-tenant/tenant.service';
import { TenantDetail, TenantSummary } from '../../../core/admin-tenant/models';

@Component({
  selector: 'app-admin-tenants',
  standalone: true,
  imports: [CommonModule, FormsModule],
  templateUrl: './admin-tenants.html',
  styleUrls: ['./admin-tenants.scss'],
})
export class AdminTenantsComponent implements OnInit {
  tenants: TenantSummary[] = [];
  selected: TenantDetail | null = null;
  loading = true;
  adminEmails = '';
  enforcementReason = '';

  constructor(private service: TenantService) {}

  ngOnInit(): void {
    this.load();
  }

  load(): void {
    this.loading = true;
    this.service.getTenants().subscribe((items) => {
      this.tenants = items;
      this.loading = false;
    });
  }

  selectTenant(tenant: TenantSummary): void {
    this.service.getTenant(tenant.id).subscribe((detail) => {
      this.selected = detail;
      this.adminEmails = detail.admins.map((a) => a.email).join(', ');
    });
  }

  saveAdmins(): void {
    if (!this.selected) return;
    const emails = this.adminEmails
      .split(',')
      .map((e) => e.trim())
      .filter(Boolean);
    this.service.updateAdmins(this.selected.id, emails).subscribe(() => {
      this.reloadSelected();
    });
  }

  suspend(): void {
    if (!this.selected) return;
    this.service.suspend(this.selected.id, this.enforcementReason || 'Suspended by admin').subscribe(() => this.reloadSelected());
  }

  ban(): void {
    if (!this.selected) return;
    this.service.ban(this.selected.id, this.enforcementReason || 'Banned by admin').subscribe(() => this.reloadSelected());
  }

  reinstate(): void {
    if (!this.selected) return;
    this.service.reinstate(this.selected.id).subscribe(() => this.reloadSelected());
  }

  private reloadSelected(): void {
    if (!this.selected) return;
    this.service.getTenant(this.selected.id).subscribe((detail) => {
      this.selected = detail;
      this.adminEmails = detail.admins.map((a) => a.email).join(', ');
      this.load();
    });
  }
}
