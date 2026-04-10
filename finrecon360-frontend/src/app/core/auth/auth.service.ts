import { HttpErrorResponse } from '@angular/common/http';
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
  isSystemAdmin?: boolean;
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
  status: string;
  isSystemAdmin: boolean;
  tenantId?: string | null;
  tenantName?: string | null;
  tenantStatus?: string | null;
  roles: string[];
  permissions: string[];
}

export interface ChangePasswordLinkResponse {
  message: string;
  emailSent?: boolean;
  cooldownActive?: boolean;
  fallbackLink?: string | null;
  deliveryError?: string | null;
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
        'ADMIN.TENANTS.MANAGE',
        'ADMIN.TENANT_REGISTRATIONS.MANAGE',
        'ADMIN.PLANS.MANAGE',
        'ADMIN.ENFORCEMENT.MANAGE',
        'MATCHER.VIEW',
        'MATCHER.MANAGE',
        'BALANCER.VIEW',
        'TASKS.VIEW',
        'JOURNAL.VIEW',
        'ANALYTICS.VIEW',
      ],
      token: 'mock-admin-token',
      isSystemAdmin: true,
    },
    {
      email: 'user@finrecon.local',
      password: 'User123!',
      displayName: 'Accountant User',
      roles: ['ACCOUNTANT'],
      permissions: ['MATCHER.VIEW', 'BALANCER.VIEW', 'TASKS.VIEW'],
      token: 'mock-user-token',
      isSystemAdmin: false,
    },
  ];

  private currentUserSubject = new BehaviorSubject<CurrentUser | null>(this.loadFromStorage());
  readonly currentUser$ = this.currentUserSubject.asObservable();

  constructor(private http: HttpClient) {
    const current = this.currentUserSubject.value;
    if (current?.token && !USE_MOCK_API) {
      this.refreshCurrentUser().subscribe({
        error: () => {
          // Keep session on non-auth startup errors (e.g. temporary backend issues).
        },
      });
    }
  }

  get currentUser(): CurrentUser | null {
    return this.currentUserSubject.value;
  }

  get isAuthenticated(): boolean {
    const user = this.currentUserSubject.value;
    if (!user) {
      return false;
    }

    if (this.isTokenExpired(user.token)) {
      this.logout();
      return false;
    }

    return true;
  }

  getAccessToken(): string | null {
    const token = this.currentUserSubject.value?.token ?? null;
    if (this.isTokenExpired(token)) {
      this.logout();
      return null;
    }

    return token;
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
        (u) => u.email.toLowerCase() === email.toLowerCase() && u.password === password,
      );

      if (!account) {
        return throwError(() => new Error('invalid-credentials'));
      }

      const user: CurrentUser = {
        id: `mock-${account.email}`,
        email: account.email,
        displayName: account.displayName,
        isSystemAdmin: account.isSystemAdmin ?? false,
        roles: account.roles,
        permissions: account.permissions,
        token: account.token,
      };

      return of(user).pipe(
        delay(250),
        tap((u) => {
          this.currentUserSubject.next(u);
          this.persist(u);
        }),
      );
    }

    return this.http
      .post<LoginResponse>(`${API_BASE_URL}${API_ENDPOINTS.AUTH.LOGIN}`, {
        email,
        password,
      })
      .pipe(
        switchMap((loginResponse) => {
          const previousUser = this.currentUserSubject.value;
          const canReuseTenantContext =
            !!previousUser &&
            previousUser.email.toLowerCase() === loginResponse.email.toLowerCase() &&
            !!previousUser.tenantId;

          const bootstrapUser: CurrentUser = {
            id: '',
            email: loginResponse.email,
            displayName: loginResponse.fullName,
            isSystemAdmin: false,
            tenantId: canReuseTenantContext ? (previousUser?.tenantId ?? null) : null,
            tenantName: canReuseTenantContext ? (previousUser?.tenantName ?? null) : null,
            tenantStatus: canReuseTenantContext ? (previousUser?.tenantStatus ?? null) : null,
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
                status: me.status,
                isSystemAdmin: me.isSystemAdmin,
                tenantId: me.tenantId ?? null,
                tenantName: me.tenantName ?? null,
                tenantStatus: me.tenantStatus ?? null,
                roles: me.roles,
                permissions: me.permissions,
                token: loginResponse.token,
              };
              this.currentUserSubject.next(updated);
              this.persist(updated);
              return updated;
            }),
          );
        }),
      );
  }

  registerTenant(payload: {
    businessName: string;
    adminEmail: string;
    phoneNumber: string;
    businessRegistrationNumber: string;
    businessType: string;
    onboardingMetadata?: unknown;
  }): Observable<void> {
    if (USE_MOCK_API) {
      return of(void 0).pipe(delay(200));
    }

    return this.http
      .post<void>(`${API_BASE_URL}${API_ENDPOINTS.PUBLIC.TENANT_REGISTRATIONS}`, payload)
      .pipe(map(() => void 0));
  }

  verifyOnboardingMagicLink(
    token: string,
  ): Observable<{ onboardingToken: string; email: string; tenantName: string }> {
    if (USE_MOCK_API) {
      return of({
        onboardingToken: 'mock',
        email: 'mock@finrecon.local',
        tenantName: 'Mock Tenant',
      }).pipe(delay(200));
    }

    return this.http.post<{ onboardingToken: string; email: string; tenantName: string }>(
      `${API_BASE_URL}${API_ENDPOINTS.ONBOARDING.VERIFY_MAGIC_LINK}`,
      { token },
    );
  }

  setOnboardingPassword(payload: {
    onboardingToken: string;
    magicLinkToken: string;
    password: string;
    confirmPassword: string;
  }): Observable<void> {
    if (USE_MOCK_API) {
      return of(void 0).pipe(delay(200));
    }

    return this.http
      .post<void>(`${API_BASE_URL}${API_ENDPOINTS.ONBOARDING.SET_PASSWORD}`, payload)
      .pipe(map(() => void 0));
  }

  createOnboardingCheckout(payload: {
    onboardingToken: string;
    planId: string;
  }): Observable<{ checkoutUrl: string }> {
    if (USE_MOCK_API) {
      return of({ checkoutUrl: 'https://example.com/checkout' }).pipe(delay(200));
    }

    return this.http.post<{ checkoutUrl: string }>(
      `${API_BASE_URL}${API_ENDPOINTS.ONBOARDING.CHECKOUT}`,
      payload,
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

  requestChangePasswordLink(): Observable<ChangePasswordLinkResponse> {
    if (USE_MOCK_API) {
      return of({
        message: 'Check your email for the password change link.',
        emailSent: true,
        cooldownActive: false,
        fallbackLink: null,
        deliveryError: null,
      }).pipe(delay(200));
    }
    return this.http.post<ChangePasswordLinkResponse>(
      `${API_BASE_URL}${API_ENDPOINTS.AUTH.REQUEST_CHANGE_PASSWORD_LINK}`,
      {},
    );
  }

  confirmChangePasswordLink(
    token: string,
    currentPassword: string,
    newPassword: string,
  ): Observable<void> {
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
          status: me.status,
          isSystemAdmin: me.isSystemAdmin,
          tenantId: me.tenantId ?? null,
          tenantName: me.tenantName ?? null,
          tenantStatus: me.tenantStatus ?? null,
          roles: me.roles,
          permissions: me.permissions,
          token: current?.token ?? null,
        };
        this.currentUserSubject.next(updated);
        this.persist(updated);
        return updated;
      }),
      catchError((err) => {
        const status = err instanceof HttpErrorResponse ? err.status : undefined;
        if (status === 401 || status === 403) {
          this.logout();
        }
        return throwError(() => err);
      }),
    );
  }

  logout(): void {
    this.currentUserSubject.next(null);
    localStorage.removeItem(this.storageKey);
    sessionStorage.removeItem('fr360_onboarding_token');
    sessionStorage.removeItem('fr360_onboarding_tenant');
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
      const parsed = JSON.parse(raw) as CurrentUser;
      if (this.isTokenExpired(parsed.token)) {
        localStorage.removeItem(this.storageKey);
        return null;
      }

      return parsed;
    } catch {
      return null;
    }
  }

  private isTokenExpired(token: string | null | undefined): boolean {
    if (!token) {
      return true;
    }

    try {
      const parts = token.split('.');
      if (parts.length !== 3) {
        return !USE_MOCK_API;
      }

      const payload = parts[1].replace(/-/g, '+').replace(/_/g, '/');
      const padded = payload + '='.repeat((4 - (payload.length % 4)) % 4);
      const decoded = atob(padded);
      const parsed = JSON.parse(decoded) as { exp?: number };

      if (typeof parsed.exp !== 'number') {
        return !USE_MOCK_API;
      }

      return Date.now() >= parsed.exp * 1000;
    } catch {
      return !USE_MOCK_API;
    }
  }
}
