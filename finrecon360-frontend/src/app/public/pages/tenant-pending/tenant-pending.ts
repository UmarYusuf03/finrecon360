import { Component } from '@angular/core';
import { CommonModule } from '@angular/common';
import { RouterModule } from '@angular/router';

@Component({
  selector: 'app-tenant-pending',
  standalone: true,
  imports: [CommonModule, RouterModule],
  templateUrl: './tenant-pending.html',
  styleUrls: ['./tenant-pending.scss'],
})
export class TenantPendingComponent {}
