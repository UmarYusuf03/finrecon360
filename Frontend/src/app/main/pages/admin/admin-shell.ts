import { Component } from '@angular/core';
import { CommonModule } from '@angular/common';
import { MatTabsModule } from '@angular/material/tabs';
import { RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { TranslateModule } from '@ngx-translate/core';

@Component({
  selector: 'app-admin-shell',
  standalone: true,
  imports: [CommonModule, RouterOutlet, RouterLink, RouterLinkActive, MatTabsModule, TranslateModule],
  templateUrl: './admin-shell.html',
  styleUrls: ['./admin-shell.scss'],
})
export class AdminShellComponent {
  // Tabs are driven by routes so backend-driven nav remains simple later.
  links = [
    { path: '/app/admin/roles', label: 'ADMIN.ROLES.TITLE' },
    { path: '/app/admin/components', label: 'ADMIN.COMPONENTS.TITLE' },
    { path: '/app/admin/permissions', label: 'ADMIN.PERMISSIONS.TITLE' },
    { path: '/app/admin/users', label: 'ADMIN.USERS.TITLE' },
  ];
}
