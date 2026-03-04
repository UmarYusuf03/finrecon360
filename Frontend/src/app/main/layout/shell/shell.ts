import { Component, OnDestroy, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { MatMenuModule } from '@angular/material/menu';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { Router, RouterLink, RouterOutlet, RouterLinkActive } from '@angular/router';
import { TranslateModule } from '@ngx-translate/core';
import { Observable, Subject } from 'rxjs';
import { takeUntil } from 'rxjs/operators';

import { HasPermissionDirective } from '../../../core/auth/has-permission.directive';
import { AuthService } from '../../../core/auth/auth.service';
import { ProfileService } from '../../services/profile.service';
import { CurrentUser } from '../../../core/auth/models';
import { LanguageSwitcherComponent } from '../../../shared/components/language-switcher/language-switcher';

@Component({
  selector: 'app-shell',
  standalone: true,
  imports: [
    CommonModule,
    RouterOutlet,
    RouterLink,
    RouterLinkActive,
    LanguageSwitcherComponent,
    TranslateModule,
    HasPermissionDirective,
    MatMenuModule,
    MatButtonModule,
    MatIconModule,
  ],
  templateUrl: './shell.html',
  styleUrls: ['./shell.scss'],
})
export class ShellComponent implements OnInit, OnDestroy {
  user$: Observable<CurrentUser | null>;
  profileImageUrl: string | null = null;

  private destroy$ = new Subject<void>();

  constructor(
    private authService: AuthService,
    private router: Router,
    private profileService: ProfileService
  ) {
    // assign here so it is initialized after DI
    this.user$ = this.authService.currentUser$;
  }

  ngOnInit(): void {
    this.authService.currentUser$
      .pipe(takeUntil(this.destroy$))
      .subscribe((user) => {
        if (!user) {
          this.clearProfileImageUrl();
          return;
        }

        this.profileService.getProfile().pipe(takeUntil(this.destroy$)).subscribe((profile) => {
          if (profile.hasProfileImage) {
            this.loadProfileImage();
          } else {
            this.clearProfileImageUrl();
          }
        });
      });
  }

  ngOnDestroy(): void {
    this.destroy$.next();
    this.destroy$.complete();
    this.clearProfileImageUrl();
  }

  getInitials(name: string | null | undefined): string {
    if (!name) return 'U';
    const parts = name.trim().split(/\s+/).filter(Boolean);
    if (!parts.length) return 'U';
    const first = parts[0]?.[0] ?? '';
    const second = parts.length > 1 ? parts[parts.length - 1]?.[0] ?? '' : '';
    return (first + second).toUpperCase();
  }

  logout(): void {
    this.authService.logout();
    this.router.navigateByUrl('/auth/login');
  }

  private loadProfileImage(): void {
    this.profileService.getProfileImage().subscribe({
      next: (blob) => {
        this.clearProfileImageUrl();
        this.profileImageUrl = URL.createObjectURL(blob);
      },
      error: () => {
        this.clearProfileImageUrl();
      },
    });
  }

  private clearProfileImageUrl(): void {
    if (this.profileImageUrl) {
      URL.revokeObjectURL(this.profileImageUrl);
      this.profileImageUrl = null;
    }
  }
}
