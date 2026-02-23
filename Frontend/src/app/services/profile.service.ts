import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable, of } from 'rxjs';
import { delay, map, tap } from 'rxjs/operators';

import { AuthService } from '../../core/auth/auth.service';
import { API_BASE_URL, API_ENDPOINTS, USE_MOCK_API } from '../../core/constants/api.constants';
import { UserProfileDetails } from '../models/profile.models';

interface UserProfileDto {
  userId: string;
  email: string;
  displayName: string | null;
  firstName: string;
  lastName: string;
  phoneNumber: string | null;
  hasProfileImage: boolean;
  isActive: boolean;
}

@Injectable({
  providedIn: 'root',
})
export class ProfileService {
  private readonly storageKey = 'fr360_profile';

  constructor(private authService: AuthService, private http: HttpClient) {}

  getProfile(): Observable<UserProfileDetails> {
    if (USE_MOCK_API) {
      const fromStorage = this.loadFromStorage();
      if (fromStorage) {
        return of(fromStorage);
      }

      const current = this.authService.currentUser;
      const mock: UserProfileDetails = {
        displayName: current?.displayName ?? 'FinRecon User',
        firstName: 'FinRecon',
        lastName: 'User',
        email: current?.email ?? 'user@finrecon.local',
        phoneNumber: null,
        roles: current?.roles ?? [],
        preferredLanguage: 'en',
        timeZone: 'UTC',
        emailNotifications: true,
        hasProfileImage: false,
      };

      return of(mock).pipe(tap((profile) => this.persist(profile)));
    }

    return this.http
      .get<UserProfileDto>(`${API_BASE_URL}${API_ENDPOINTS.USERS.PROFILE}`)
      .pipe(map((dto) => this.mapProfile(dto)));
  }

  updateProfile(profile: UserProfileDetails): Observable<UserProfileDetails> {
    if (USE_MOCK_API) {
      return of(profile).pipe(
        delay(150),
        tap((p) => {
          this.persist(p);
          this.authService.updateCurrentUser({ displayName: p.displayName });
        })
      );
    }

    const request = {
      displayName: profile.displayName,
      firstName: profile.firstName,
      lastName: profile.lastName,
      phoneNumber: profile.phoneNumber,
    };

    return this.http
      .put<UserProfileDto>(`${API_BASE_URL}${API_ENDPOINTS.USERS.PROFILE}`, request)
      .pipe(
        map((dto) => this.mapProfile(dto)),
        tap((updated) => this.authService.updateCurrentUser({ displayName: updated.displayName }))
      );
  }

  uploadProfileImage(file: File): Observable<void> {
    if (USE_MOCK_API) {
      return of(void 0).pipe(delay(150));
    }

    const formData = new FormData();
    formData.append('file', file);

    return this.http.post<void>(`${API_BASE_URL}${API_ENDPOINTS.USERS.PROFILE}/photo`, formData);
  }

  deleteProfileImage(): Observable<void> {
    if (USE_MOCK_API) {
      return of(void 0).pipe(delay(150));
    }

    return this.http.delete<void>(`${API_BASE_URL}${API_ENDPOINTS.USERS.PROFILE}/photo`);
  }

  getProfileImage(): Observable<Blob> {
    return this.http.get(`${API_BASE_URL}${API_ENDPOINTS.USERS.PROFILE}/photo`, {
      responseType: 'blob',
    });
  }

  deleteAccount(): Observable<void> {
    if (USE_MOCK_API) {
      return of(void 0).pipe(delay(150));
    }

    return this.http.post<void>(`${API_BASE_URL}${API_ENDPOINTS.USERS.PROFILE}/delete`, {});
  }

  private mapProfile(dto: UserProfileDto): UserProfileDetails {
    const displayName = dto.displayName ?? `${dto.firstName} ${dto.lastName}`.trim();
    return {
      displayName,
      firstName: dto.firstName,
      lastName: dto.lastName,
      email: dto.email,
      phoneNumber: dto.phoneNumber,
      roles: [],
      preferredLanguage: 'en',
      timeZone: 'UTC',
      emailNotifications: true,
      hasProfileImage: dto.hasProfileImage,
    };
  }

  private loadFromStorage(): UserProfileDetails | null {
    const raw = localStorage.getItem(this.storageKey);
    if (!raw) return null;
    try {
      return JSON.parse(raw) as UserProfileDetails;
    } catch {
      return null;
    }
  }

  private persist(profile: UserProfileDetails): void {
    localStorage.setItem(this.storageKey, JSON.stringify(profile));
  }
}
