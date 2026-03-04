import { Component, OnDestroy, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormBuilder, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { MatCardModule } from '@angular/material/card';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatSelectModule } from '@angular/material/select';
import { MatSlideToggleModule } from '@angular/material/slide-toggle';
import { MatChipsModule } from '@angular/material/chips';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { RouterModule, Router } from '@angular/router';
import { TranslateModule, TranslateService } from '@ngx-translate/core';
import { Subject, takeUntil } from 'rxjs';

import { AuthService } from '../../../core/auth/auth.service';
import { SUPPORTED_LANGUAGES } from '../../../core/constants/languages';
import { UserProfileDetails } from '../../models/profile.models';
import { ProfileService } from '../../services/profile.service';

const TIME_ZONES = ['UTC', 'America/New_York', 'Europe/London', 'Asia/Colombo'];

@Component({
  selector: 'app-profile',
  standalone: true,
  imports: [
    CommonModule,
    ReactiveFormsModule,
    MatCardModule,
    MatFormFieldModule,
    MatInputModule,
    MatSelectModule,
    MatSlideToggleModule,
    MatChipsModule,
    MatButtonModule,
    MatIconModule,
    RouterModule,
    TranslateModule,
  ],
  templateUrl: './profile.html',
  styleUrls: ['./profile.scss'],
})
export class ProfileComponent implements OnInit, OnDestroy {
  form!: FormGroup;
  roles: string[] = [];
  languages = SUPPORTED_LANGUAGES;
  timeZones = TIME_ZONES;

  changePasswordStatus: string | null = null;
  changePasswordSubmitting = false;

  profileImageUrl: string | null = null;
  profileImageStatus: string | null = null;
  profileImageUploading = false;
  profileImageRemoving = false;

  deleteStatus: string | null = null;
  deleteSubmitting = false;

  private destroy$ = new Subject<void>();

  constructor(
    private fb: FormBuilder,
    private profileService: ProfileService,
    private translate: TranslateService,
    private authService: AuthService,
    private router: Router
  ) {}

  ngOnInit(): void {
    this.form = this.fb.group({
      firstName: ['', Validators.required],
      lastName: ['', Validators.required],
      displayName: ['', Validators.required],
      email: [{ value: '', disabled: true }],
      phoneNumber: [''],
      preferredLanguage: ['', Validators.required],
      timeZone: ['', Validators.required],
      emailNotifications: [true],
    });

    this.profileService
      .getProfile()
      .pipe(takeUntil(this.destroy$))
      .subscribe((profile) => {
        this.setForm(profile);
        if (profile.hasProfileImage) {
          this.loadProfileImage();
        }
      });
  }

  ngOnDestroy(): void {
    this.destroy$.next();
    this.destroy$.complete();
    this.clearProfileImageUrl();
  }

  save(): void {
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }
    const payload: UserProfileDetails = {
      displayName: this.form.get('displayName')?.value,
      firstName: this.form.get('firstName')?.value,
      lastName: this.form.get('lastName')?.value,
      email: this.form.get('email')?.value,
      phoneNumber: this.form.get('phoneNumber')?.value || null,
      roles: this.roles,
      preferredLanguage: this.form.get('preferredLanguage')?.value,
      timeZone: this.form.get('timeZone')?.value,
      emailNotifications: this.form.get('emailNotifications')?.value,
      hasProfileImage: this.profileImageUrl !== null,
    };

    this.profileService
      .updateProfile(payload)
      .pipe(takeUntil(this.destroy$))
      .subscribe((updated) => {
        this.translate.use(updated.preferredLanguage);
      });
  }

  confirmChangePasswordLink(): void {
    const email = this.form.get('email')?.value as string;
    const masked = this.maskEmail(email);
    if (!confirm(`Send a password change link to ${masked}?`)) {
      return;
    }

    this.changePasswordSubmitting = true;
    this.changePasswordStatus = null;

    this.authService.requestChangePasswordLink().subscribe({
      next: () => {
        this.changePasswordSubmitting = false;
        this.changePasswordStatus = 'Check your email for the password change link.';
      },
      error: () => {
        this.changePasswordSubmitting = false;
        this.changePasswordStatus = 'If the email exists, you will receive a link shortly.';
      },
    });
  }

  onProfileImageSelected(event: Event): void {
    const input = event.target as HTMLInputElement;
    if (!input.files || !input.files.length) {
      return;
    }

    const file = input.files[0];
    this.profileImageStatus = null;

    if (!file.type.startsWith('image/')) {
      this.profileImageStatus = 'Please select an image file.';
      return;
    }

    if (file.size > 2 * 1024 * 1024) {
      this.profileImageStatus = 'Profile image must be under 2MB.';
      return;
    }

    this.profileImageUploading = true;
    this.profileService.uploadProfileImage(file).subscribe({
      next: () => {
        this.profileImageUploading = false;
        this.profileImageStatus = 'Profile image updated.';
        this.loadProfileImage();
      },
      error: () => {
        this.profileImageUploading = false;
        this.profileImageStatus = 'Failed to upload profile image.';
      },
    });
  }

  removeProfileImage(): void {
    this.profileImageRemoving = true;
    this.profileService.deleteProfileImage().subscribe({
      next: () => {
        this.profileImageRemoving = false;
        this.profileImageStatus = 'Profile image removed.';
        this.clearProfileImageUrl();
      },
      error: () => {
        this.profileImageRemoving = false;
        this.profileImageStatus = 'Failed to remove profile image.';
      },
    });
  }

  deleteAccount(): void {
    if (!confirm('Are you sure you want to deactivate your account?')) {
      return;
    }

    this.deleteSubmitting = true;
    this.deleteStatus = null;

    this.profileService.deleteAccount().subscribe({
      next: () => {
        this.deleteSubmitting = false;
        this.deleteStatus = 'Your account has been deactivated.';
        this.authService.logout();
        this.router.navigateByUrl('/auth/login');
      },
      error: () => {
        this.deleteSubmitting = false;
        this.deleteStatus = 'Unable to deactivate your account. Please try again.';
      },
    });
  }


  getInitials(name: string | null | undefined): string {
    if (!name) return 'U';
    const parts = name.trim().split(/\s+/).filter(Boolean);
    if (!parts.length) return 'U';
    const first = parts[0]?.[0] ?? '';
    const second = parts.length > 1 ? parts[parts.length - 1]?.[0] ?? '' : '';
    return (first + second).toUpperCase();
  }

  private maskEmail(email: string | null | undefined): string {
    if (!email || !email.includes('@')) return 'your email';
    const [user, domain] = email.split('@');
    const maskedUser = user.length <= 2
      ? `${user[0] ?? ''}*`
      : `${user[0]}${'*'.repeat(Math.max(1, user.length - 2))}${user[user.length - 1]}`;
    const domainParts = domain.split('.');
    const domainName = domainParts[0] ?? '';
    const tld = domainParts.slice(1).join('.') || '';
    const maskedDomain = domainName.length <= 2
      ? `${domainName[0] ?? ''}*`
      : `${domainName[0]}${'*'.repeat(Math.max(1, domainName.length - 2))}${domainName[domainName.length - 1]}`;
    return `${maskedUser}@${maskedDomain}${tld ? '.' + tld : ''}`;
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

  private setForm(profile: UserProfileDetails): void {
    this.roles = profile.roles;
    this.form.patchValue({
      displayName: profile.displayName,
      firstName: profile.firstName,
      lastName: profile.lastName,
      email: profile.email,
      phoneNumber: profile.phoneNumber ?? '',
      preferredLanguage: profile.preferredLanguage,
      timeZone: profile.timeZone,
      emailNotifications: profile.emailNotifications,
    });
  }
}
