import { CommonModule } from '@angular/common';
import { Component, OnInit } from '@angular/core';
import { MatTabsModule } from '@angular/material/tabs';
import {
  ActivatedRoute,
  Router,
  RouterLink,
  RouterLinkActive,
  RouterOutlet,
} from '@angular/router';
import { TranslateModule } from '@ngx-translate/core';
import { Observable } from 'rxjs';
import { map, take } from 'rxjs/operators';

import { AuthService } from '../../../core/auth/auth.service';

type ImportLink = {
  path: string;
  label: string;
  permission: string;
};

@Component({
  selector: 'app-imports-shell',
  standalone: true,
  imports: [CommonModule, RouterOutlet, RouterLink, RouterLinkActive, MatTabsModule, TranslateModule],
  templateUrl: './imports-shell.html',
})
export class ImportsShellComponent implements OnInit {
  private readonly links: ImportLink[] = [
    {
      path: '/app/imports/workbench',
      label: 'Workbench',
      permission: 'ADMIN.IMPORT_WORKBENCH.VIEW',
    },
    {
      path: '/app/imports/import-architecture',
      label: 'ADMIN.IMPORT_ARCHITECTURE.TITLE',
      permission: 'ADMIN.IMPORT_ARCHITECTURE.VIEW',
    },
    {
      path: '/app/imports/import-history',
      label: 'ADMIN.IMPORT_HISTORY.TITLE',
      permission: 'ADMIN.IMPORT_ARCHITECTURE.VIEW',
    },
  ];

  readonly visibleLinks$: Observable<ImportLink[]>;

  constructor(
    private readonly authService: AuthService,
    private readonly router: Router,
    private readonly route: ActivatedRoute,
  ) {
    this.visibleLinks$ = this.authService.currentUser$.pipe(
      map((user) => {
        if (!user || user.isSystemAdmin) {
          return [] as ImportLink[];
        }

        return this.links.filter((link) => this.hasPermission(user.permissions, link.permission));
      }),
    );
  }

  ngOnInit(): void {
    this.visibleLinks$.pipe(take(1)).subscribe((links) => {
      const onImportsRoot = this.router.url === '/app/imports' || this.router.url === '/app/imports/';
      if (!onImportsRoot) {
        return;
      }

      if (links.length > 0) {
        this.router.navigateByUrl(links[0].path);
        return;
      }

      this.router.navigate(['/app/not-authorized'], { relativeTo: this.route.root });
    });
  }

  private hasPermission(grantedPermissions: string[], requiredPermission: string): boolean {
    if (grantedPermissions.includes(requiredPermission)) {
      return true;
    }

    const separatorIndex = requiredPermission.lastIndexOf('.');
    if (separatorIndex <= 0) {
      return false;
    }

    const manageCode = `${requiredPermission.slice(0, separatorIndex)}.MANAGE`;
    return grantedPermissions.includes(manageCode);
  }
}
