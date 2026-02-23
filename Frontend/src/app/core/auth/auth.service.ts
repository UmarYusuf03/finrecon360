import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { BehaviorSubject, Observable, of, throwError } from 'rxjs';
import { catchError, delay, map, switchMap, tap } from 'rxjs/operators';

import { API_BASE_URL, API_ENDPOINTS, USE_MOCK_API } from '../constants/api.constants';
import { CurrentUser, PermissionCode, RoleCode } from './models';

export interface LoginCredentials {
  email: string;
  password: string;
}

interface MockAccount {
  email: string;
  password: string;
  displayName: string;
  roles: RoleCode[];
  permissions: PermissionCode[];
  token: string;
}

interface LoginResponse {
  email: string;
  fullName: string;
  token: string;
}

interface MeResponse {
  userId: string;
  email: string;
  displayName: string | null;
  roles: string[];
  permissions: string[];
}

@Injectable({
  providedIn: 'root',
})
export class AuthService {
  private readonly storageKey = 'fr360_current_user';

  private readonly mockAccounts: MockAccount[] = [
    {
      email: 'admin@finrecon.local',
      password: 'Admin123!',
      displayName: 'System Admin',
      roles: ['ADMIN'],
      permissions: [
        'ADMIN.DASHBOARD.VIEW',
        'ADMIN.ROLES.MANAGE',
        'ADMIN.PERMISSIONS.MANAGE',
        'ADMIN.COMPONENTS.MANAGE',
        'ADMIN.USERS.MANAGE',
        'MATCHER.VIEW',
        'MATCHER.MANAGE',
        'BALANCER.VIEW',
        'TASKS.VIEW',
        'JOURNAL.VIEW',
        'ANALYTICS.VIEW',
      ],
      token: 'mock-admin-token',
    },
    {
      email: 'user@finrecon.local',
      password: 'User123!',
      displayName: 'Accountant User',
      roles: ['ACCOUNTANT'],
      permissions: ['MATCHER.VIEW', 'BALANCER.VIEW', 'TASKS.VIEW'],
      token: 'mock-user-token',
    },
  ];

  private currentUserSubject = new BehaviorSubject<CurrentUser | null>(this.loadFromStorage());
  readonly currentUser$ = this.currentUserSubject.asObservable();

  constructor(private http: HttpClient) {
    const current = this.currentUserSubject.value;
    if (current?.token && !USE_MOCK_API) {
      this.refreshCurrentUser().subscribe({
        error: () => {
          this.logout();
        },
      });
    }
  }

  get currentUser(): CurrentUser | null {
    return this.currentUserSubject.value;
  }

  get isAuthenticated(): boolean {
    return !!this.currentUserSubject.value;
  }

  getAccessToken(): string | null {
    return this.currentUserSubject.value?.token ?? null;
  }

  updateCurrentUser(patch: Partial<CurrentUser>): void {
    const current = this.currentUserSubject.value;
    if (!current) return;
    const updated = { ...current, ...patch };
    this.currentUserSubject.next(updated);
    this.persist(updated);
  }

  login(email: string, password: string): Observable<CurrentUser> {
    if (USE_MOCK_API) {
      const account = this.mockAccounts.find(
        (u) => u.email.toLowerCase() === email.toLowerCase() && u.password === password
      );

      if (!account) {
        return throwError(() => new Error('invalid-credentials'));
      }

      const user: CurrentUser = {
        id: `mock-${account.email}`,
        email: account.email,
        displayName: account.displayName,
        roles: account.roles,
        permissions: account.permissions,
        token: account.token,
      };

      return of(user).pipe(
        delay(250),
        tap((u) => {
          this.currentUserSubject.next(u);
          this.persist(u);
        })
      );
    }

    return this.http
      .post<LoginResponse>(`${API_BASE_URL}${API_ENDPOINTS.AUTH.LOGIN}`, {
        email,
        password,
      })
      .pipe(
        switchMap((loginResponse) => {
          const bootstrapUser: CurrentUser = {
            id: '',
            email: loginResponse.email,
            displayName: loginResponse.fullName,
            roles: [],
            permissions: [],
            token: loginResponse.token,
          };
          this.currentUserSubject.next(bootstrapUser);
          this.persist(bootstrapUser);

          return this.fetchMe().pipe(
            map((me) => {
              const updated: CurrentUser = {
                id: me.userId,
                email: me.email,
                displayName: me.displayName ?? loginResponse.fullName,
                roles: me.roles,
                permissions: me.permissions,
                token: loginResponse.token,
              };
              this.currentUserSubject.next(updated);
              this.persist(updated);
              return updated;
            })
          );
        })
      );
  }

  register(payload: {
    email: string;
    firstName: string;
    lastName: string;
    country: string;
    gender: string;
    password: string;
    confirmPassword: string;
  }): Observable<void> {
    if (USE_MOCK_API) {
      return of(void 0).pipe(delay(200));
    }

    return this.http
      .post<void>(`${API_BASE_URL}${API_ENDPOINTS.AUTH.REGISTER}`, payload)
      .pipe(map(() => void 0));
  }

  verifyEmailLink(token: string): Observable<void> {
    if (USE_MOCK_API) {
      return of(void 0).pipe(delay(200));
    }
    return this.http
      .post<void>(`${API_BASE_URL}${API_ENDPOINTS.AUTH.VERIFY_EMAIL_LINK}`, { token })
      .pipe(map(() => void 0));
  }

  requestPasswordResetLink(email: string): Observable<void> {
    if (USE_MOCK_API) {
      return of(void 0).pipe(delay(200));
    }
    return this.http
      .post<void>(`${API_BASE_URL}${API_ENDPOINTS.AUTH.REQUEST_PASSWORD_RESET_LINK}`, { email })
      .pipe(map(() => void 0));
  }

  confirmPasswordResetLink(token: string, newPassword: string): Observable<void> {
    if (USE_MOCK_API) {
      return of(void 0).pipe(delay(200));
    }
    return this.http
      .post<void>(`${API_BASE_URL}${API_ENDPOINTS.AUTH.CONFIRM_PASSWORD_RESET_LINK}`, {
        token,
        newPassword,
      })
      .pipe(map(() => void 0));
  }

  requestChangePasswordLink(): Observable<void> {
    if (USE_MOCK_API) {
      return of(void 0).pipe(delay(200));
    }
    return this.http
      .post<void>(`${API_BASE_URL}${API_ENDPOINTS.AUTH.REQUEST_CHANGE_PASSWORD_LINK}`, {})
      .pipe(map(() => void 0));
  }

  confirmChangePasswordLink(token: string, currentPassword: string, newPassword: string): Observable<void> {
    if (USE_MOCK_API) {
      return of(void 0).pipe(delay(200));
    }
    return this.http
      .post<void>(`${API_BASE_URL}${API_ENDPOINTS.AUTH.CONFIRM_CHANGE_PASSWORD_LINK}`, {
        token,
        currentPassword,
        newPassword,
      })
      .pipe(map(() => void 0));
  }

  refreshCurrentUser(): Observable<CurrentUser> {
    if (USE_MOCK_API) {
      const current = this.currentUserSubject.value;
      if (!current) {
        return throwError(() => new Error('not-authenticated'));
      }
      return of(current);
    }

    return this.fetchMe().pipe(
      map((me) => {
        const current = this.currentUserSubject.value;
        const updated: CurrentUser = {
          id: me.userId,
          email: me.email,
          displayName: me.displayName ?? current?.displayName ?? me.email,
          roles: me.roles,
          permissions: me.permissions,
          token: current?.token ?? null,
        };
        this.currentUserSubject.next(updated);
        this.persist(updated);
        return updated;
      }),
      catchError((err) => {
        this.logout();
        return throwError(() => err);
      })
    );
  }

  logout(): void {
    this.currentUserSubject.next(null);
    localStorage.removeItem(this.storageKey);
  }

  private fetchMe(): Observable<MeResponse> {
    return this.http.get<MeResponse>(`${API_BASE_URL}${API_ENDPOINTS.ME}`);
  }

  private persist(user: CurrentUser): void {
    localStorage.setItem(this.storageKey, JSON.stringify(user));
  }

  private loadFromStorage(): CurrentUser | null {
    const raw = localStorage.getItem(this.storageKey);
    if (!raw) return null;
    try {
      return JSON.parse(raw) as CurrentUser;
    } catch {
      return null;
    }
  }
}
