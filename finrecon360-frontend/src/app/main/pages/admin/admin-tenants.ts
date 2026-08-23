import { Component, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute } from '@angular/router';
import { TranslateModule } from '@ngx-translate/core';

import { TenantService } from '../../../core/admin-tenant/tenant.service';
import { TenantAdmin, TenantDetail, TenantSummary } from '../../../core/admin-tenant/models';
import { AdminUserService } from '../../../core/admin-rbac/admin-user.service';
import { ConfirmDialogComponent, ConfirmDialogVariant } from '../../../shared/components/confirm-dialog/confirm-dialog';

type EnforcementAction = 'suspend' | 'ban' | 'suspendAdmin' | 'banAdmin';

@Component({
  selector: 'app-admin-tenants',
  standalone: true,
  imports: [CommonModule, FormsModule, TranslateModule, ConfirmDialogComponent],
  templateUrl: './admin-tenants.html',
  styleUrls: ['./admin-tenants.scss'],
})
export class AdminTenantsComponent implements OnInit {
  tenants: TenantSummary[] = [];
  searchTerm = '';

  get filteredTenants(): TenantSummary[] {
    if (!this.searchTerm) {
      return this.tenants;
    }
    const lowerTerm = this.searchTerm.toLowerCase();
    return this.tenants.filter((t) => t.name.toLowerCase().includes(lowerTerm));
  }

  selected: TenantDetail | null = null;
  loading = true;
  processing = false;
  adminEmails = '';
  enforcementReason = '';
  actionMessage: string | null = null;
  actionError: string | null = null;
  deleteDialogOpen = false;
  deleteTargetName = '';
  deleteTargetId = '';
  adminActionId: string | null = null;

  confirmEnforcementOpen = false;
  confirmEnforcementAction: EnforcementAction | null = null;
  confirmEnforcementAdmin: TenantAdmin | null = null;

  constructor(
    private service: TenantService,
    private userService: AdminUserService,
    private route: ActivatedRoute,
  ) {}

  ngOnInit(): void {
    this.load();
  }

  load(): void {
    this.loading = true;
    this.actionError = null;
    this.service.getTenants().subscribe((items) => {
      this.tenants = items;
      this.loading = false;

      const focusTenantId = this.route.snapshot.queryParamMap.get('tenantId');
      const focusTenant = focusTenantId ? items.find((t) => t.id === focusTenantId) : null;
      if (focusTenant) {
        this.selectTenant(focusTenant);
      }
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

  requestSuspend(): void {
    if (!this.selected) return;
    this.confirmEnforcementAction = 'suspend';
    this.confirmEnforcementAdmin = null;
    this.confirmEnforcementOpen = true;
  }

  requestBan(): void {
    if (!this.selected) return;
    this.confirmEnforcementAction = 'ban';
    this.confirmEnforcementAdmin = null;
    this.confirmEnforcementOpen = true;
  }

  reinstate(): void {
    if (!this.selected) return;
    this.service.reinstate(this.selected.id).subscribe(() => this.reloadSelected());
  }

  requestSuspendAdmin(admin: TenantAdmin): void {
    if (!this.selected || this.adminActionId) return;
    this.confirmEnforcementAction = 'suspendAdmin';
    this.confirmEnforcementAdmin = admin;
    this.confirmEnforcementOpen = true;
  }

  requestBanAdmin(admin: TenantAdmin): void {
    if (!this.selected || this.adminActionId) return;
    this.confirmEnforcementAction = 'banAdmin';
    this.confirmEnforcementAdmin = admin;
    this.confirmEnforcementOpen = true;
  }

  reinstateAdmin(admin: TenantAdmin): void {
    if (!this.selected || this.adminActionId) return;
    this.adminActionId = admin.userId;
    this.userService.reinstateUser(this.selected.id, admin.userId).subscribe({
      next: () => this.reloadSelected(),
      error: (error) => this.handleAdminActionError(error),
      complete: () => (this.adminActionId = null),
    });
  }

  closeEnforcementDialog(): void {
    if (this.adminActionId) return;
    this.confirmEnforcementOpen = false;
    this.confirmEnforcementAction = null;
    this.confirmEnforcementAdmin = null;
  }

  confirmEnforcement(): void {
    const action = this.confirmEnforcementAction;
    const admin = this.confirmEnforcementAdmin;
    this.confirmEnforcementOpen = false;
    this.confirmEnforcementAction = null;
    this.confirmEnforcementAdmin = null;

    switch (action) {
      case 'suspend':
        if (!this.selected) return;
        this.service.suspend(this.selected.id, this.enforcementReason || 'Suspended by admin').subscribe(() => this.reloadSelected());
        break;
      case 'ban':
        if (!this.selected) return;
        this.service.ban(this.selected.id, this.enforcementReason || 'Banned by admin').subscribe(() => this.reloadSelected());
        break;
      case 'suspendAdmin':
        if (!this.selected || !admin) return;
        this.adminActionId = admin.userId;
        this.userService.suspendUser(this.selected.id, admin.userId, this.enforcementReason || 'Suspended by admin').subscribe({
          next: () => this.reloadSelected(),
          error: (error) => this.handleAdminActionError(error),
          complete: () => (this.adminActionId = null),
        });
        break;
      case 'banAdmin':
        if (!this.selected || !admin) return;
        this.adminActionId = admin.userId;
        this.userService.banUser(this.selected.id, admin.userId, this.enforcementReason || 'Banned by admin').subscribe({
          next: () => this.reloadSelected(),
          error: (error) => this.handleAdminActionError(error),
          complete: () => (this.adminActionId = null),
        });
        break;
    }
  }

  get enforcementTitleKey(): string {
    switch (this.confirmEnforcementAction) {
      case 'suspend':
        return 'ADMIN.TENANTS.CONFIRM_SUSPEND_TITLE';
      case 'ban':
        return 'ADMIN.TENANTS.CONFIRM_BAN_TITLE';
      case 'suspendAdmin':
        return 'ADMIN.TENANTS.CONFIRM_SUSPEND_ADMIN_TITLE';
      case 'banAdmin':
        return 'ADMIN.TENANTS.CONFIRM_BAN_ADMIN_TITLE';
      default:
        return '';
    }
  }

  get enforcementMessageKey(): string {
    switch (this.confirmEnforcementAction) {
      case 'suspend':
        return 'ADMIN.TENANTS.CONFIRM_SUSPEND_MESSAGE';
      case 'ban':
        return 'ADMIN.TENANTS.CONFIRM_BAN_MESSAGE';
      case 'suspendAdmin':
        return 'ADMIN.TENANTS.CONFIRM_SUSPEND_ADMIN_MESSAGE';
      case 'banAdmin':
        return 'ADMIN.TENANTS.CONFIRM_BAN_ADMIN_MESSAGE';
      default:
        return '';
    }
  }

  get enforcementTargetName(): string {
    if (this.confirmEnforcementAdmin) return this.confirmEnforcementAdmin.email;
    return this.selected?.name ?? '';
  }

  get enforcementVariant(): ConfirmDialogVariant {
    return this.confirmEnforcementAction === 'ban' || this.confirmEnforcementAction === 'banAdmin' ? 'danger' : 'warning';
  }

  get enforcementConfirmLabelKey(): string {
    return this.confirmEnforcementAction === 'ban' || this.confirmEnforcementAction === 'banAdmin'
      ? 'ADMIN.TENANTS.BAN'
      : 'ADMIN.TENANTS.SUSPEND';
  }

  get enforcementProcessing(): boolean {
    return this.confirmEnforcementAdmin ? this.adminActionId !== null : this.processing;
  }

  private handleAdminActionError(error: unknown): void {
    this.adminActionId = null;
    const message = (error as { error?: { message?: string } })?.error?.message;
    this.actionMessage = null;
    this.actionError = message ?? 'Unable to update user status. Please try again.';
  }

  deleteSelectedTenant(): void {
    if (!this.selected) return;
    this.deleteTargetName = this.selected.name;
    this.deleteTargetId = this.selected.id;
    this.deleteDialogOpen = true;
  }

  closeDeleteDialog(): void {
    if (this.processing) return;
    this.deleteDialogOpen = false;
    this.deleteTargetName = '';
    this.deleteTargetId = '';
  }

  confirmDeleteTenant(): void {
    if (!this.deleteTargetId) return;
    const tenantName = this.deleteTargetName;
    const tenantId = this.deleteTargetId;
    this.processing = true;
    this.service.deleteTenant(tenantId).subscribe({
      next: () => {
        this.processing = false;
        this.closeDeleteDialog();
        this.actionError = null;
        this.actionMessage = `Tenant "${tenantName}" deleted.`;
        this.selected = null;
        this.adminEmails = '';
        this.enforcementReason = '';
        this.load();
      },
      error: (error) => {
        this.processing = false;
        const message = error?.error?.message as string | undefined;
        this.actionMessage = null;
        this.actionError = message ?? 'Unable to delete tenant. Please try again.';
      },
    });
  }

  private reloadSelected(): void {
    if (!this.selected) return;
    this.service.getTenant(this.selected.id).subscribe((detail) => {
      this.selected = detail;
      this.adminEmails = detail.admins.map((a) => a.email).join(', ');
      this.actionMessage = null;
      this.actionError = null;
      this.load();
    });
  }
}
