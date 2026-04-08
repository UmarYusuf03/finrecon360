import { Component } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';

import { TenantService } from '../../../core/admin-tenant/tenant.service';
import { AdminUserService } from '../../../core/admin-rbac/admin-user.service';

@Component({
  selector: 'app-admin-enforcement',
  standalone: true,
  imports: [CommonModule, FormsModule],
  templateUrl: './admin-enforcement.html',
  styleUrls: ['./admin-enforcement.scss'],
})
export class AdminEnforcementComponent {
  tenantId = '';
  userId = '';
  reason = '';
  message = '';

  constructor(private tenantService: TenantService, private userService: AdminUserService) {}

  suspendTenant(): void {
    this.tenantService.suspend(this.tenantId, this.reason || 'Suspended by admin').subscribe(() => {
      this.message = 'Tenant suspended.';
    });
  }

  banTenant(): void {
    this.tenantService.ban(this.tenantId, this.reason || 'Banned by admin').subscribe(() => {
      this.message = 'Tenant banned.';
    });
  }

  reinstateTenant(): void {
    this.tenantService.reinstate(this.tenantId).subscribe(() => {
      this.message = 'Tenant reinstated.';
    });
  }

  suspendUser(): void {
    this.userService.suspendUser(this.tenantId, this.userId, this.reason || 'Suspended by admin').subscribe(() => {
      this.message = 'User suspended.';
    });
  }

  banUser(): void {
    this.userService.banUser(this.tenantId, this.userId, this.reason || 'Banned by admin').subscribe(() => {
      this.message = 'User banned.';
    });
  }

  reinstateUser(): void {
    this.userService.reinstateUser(this.tenantId, this.userId).subscribe(() => {
      this.message = 'User reinstated.';
    });
  }
}
