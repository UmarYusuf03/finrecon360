import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { BehaviorSubject, Observable, of } from 'rxjs';
import { map, tap } from 'rxjs/operators';

import { API_BASE_URL, API_ENDPOINTS, USE_MOCK_API } from '../constants/api.constants';
import { AdminUserSummary, PagedResult } from './models';

interface AdminUserDto {
  id: string;
  email: string;
  displayName: string;
  isActive: boolean;
  roles: string[];
}

@Injectable({
  providedIn: 'root',
})
export class AdminUserService {
  private readonly usersSubject = new BehaviorSubject<AdminUserSummary[]>([
    {
      id: 'user-admin',
      email: 'admin@finrecon.local',
      displayName: 'Avery Admin',
      isActive: true,
      roles: ['ADMIN'],
    },
    {
      id: 'user-accountant',
      email: 'user@finrecon.local',
      displayName: 'Alex Accountant',
      isActive: true,
      roles: ['ACCOUNTANT'],
    },
  ]);
  private loaded = false;

  constructor(private http: HttpClient) {}

  getUsers(): Observable<AdminUserSummary[]> {
    if (USE_MOCK_API) {
      return this.usersSubject.asObservable();
    }

    if (!this.loaded) {
      this.loadUsers();
    }

    return this.usersSubject.asObservable();
  }

  createUser(payload: Partial<AdminUserSummary> & { password?: string }): Observable<AdminUserSummary> {
    if (USE_MOCK_API) {
      const newUser: AdminUserSummary = {
        id: `u-${Date.now()}-${Math.random()}`,
        email: payload.email ?? '',
        displayName: payload.displayName ?? 'New User',
        isActive: true,
        roles: payload.roles ?? [],
      };
      this.usersSubject.next([...this.usersSubject.value, newUser]);
      return of(newUser);
    }

    const request = {
      email: payload.email,
      displayName: payload.displayName,
      password: payload.password ?? 'TempPass123!',
      roleCodes: payload.roles ?? [],
    };

    return this.http
      .post<AdminUserDto>(`${API_BASE_URL}${API_ENDPOINTS.ADMIN.USERS}`, request)
      .pipe(
        map((dto) => this.mapUser(dto)),
        tap((user) => this.usersSubject.next([...this.usersSubject.value, user]))
      );
  }

  updateUser(id: string, payload: Partial<AdminUserSummary>): Observable<AdminUserSummary> {
    if (USE_MOCK_API) {
      const updatedList = this.usersSubject.value.map((user) =>
        user.id === id ? { ...user, ...payload } : user
      );
      const updated = updatedList.find((u) => u.id === id)!;
      this.usersSubject.next(updatedList);
      return of(updated);
    }

    const request = {
      displayName: payload.displayName ?? '',
      phoneNumber: null,
    };

    return this.http
      .put<AdminUserDto>(`${API_BASE_URL}${API_ENDPOINTS.ADMIN.USERS}/${id}`, request)
      .pipe(
        map((dto) => this.mapUser(dto)),
        tap((updated) => {
          const updatedList = this.usersSubject.value.map((user) =>
            user.id === id ? updated : user
          );
          this.usersSubject.next(updatedList);
        })
      );
  }

  setUserRoles(id: string, roles: string[]): Observable<void> {
    if (USE_MOCK_API) {
      this.usersSubject.next(
        this.usersSubject.value.map((user) =>
          user.id === id ? { ...user, roles: roles as AdminUserSummary['roles'] } : user
        )
      );
      return of(void 0);
    }

    return this.http
      .put<void>(`${API_BASE_URL}${API_ENDPOINTS.ADMIN.USERS}/${id}/roles`, { roleCodes: roles })
      .pipe(
        tap(() => {
          this.usersSubject.next(
            this.usersSubject.value.map((user) =>
              user.id === id ? { ...user, roles: roles as AdminUserSummary['roles'] } : user
            )
          );
        })
      );
  }

  deactivateUser(id: string): Observable<void> {
    if (USE_MOCK_API) {
      this.usersSubject.next(
        this.usersSubject.value.map((user) =>
          user.id === id ? { ...user, isActive: false } : user
        )
      );
      return of(void 0);
    }

    return this.http
      .post<void>(`${API_BASE_URL}${API_ENDPOINTS.ADMIN.USERS}/${id}/deactivate`, {})
      .pipe(
        tap(() => {
          this.usersSubject.next(
            this.usersSubject.value.map((user) =>
              user.id === id ? { ...user, isActive: false } : user
            )
          );
        })
      );
  }

  reactivateUser(id: string): Observable<void> {
    if (USE_MOCK_API) {
      this.usersSubject.next(
        this.usersSubject.value.map((user) =>
          user.id === id ? { ...user, isActive: true } : user
        )
      );
      return of(void 0);
    }

    return this.http
      .post<void>(`${API_BASE_URL}${API_ENDPOINTS.ADMIN.USERS}/${id}/activate`, {})
      .pipe(
        tap(() => {
          this.usersSubject.next(
            this.usersSubject.value.map((user) =>
              user.id === id ? { ...user, isActive: true } : user
            )
          );
        })
      );
  }

  private loadUsers(): void {
    this.loaded = true;
    this.http
      .get<PagedResult<AdminUserDto>>(`${API_BASE_URL}${API_ENDPOINTS.ADMIN.USERS}?page=1&pageSize=100`)
      .pipe(map((result) => result.items.map((dto) => this.mapUser(dto))))
      .subscribe((users) => this.usersSubject.next(users));
  }

  private mapUser(dto: AdminUserDto): AdminUserSummary {
    return {
      id: dto.id,
      email: dto.email,
      displayName: dto.displayName,
      isActive: dto.isActive,
      roles: dto.roles,
    };
  }
}
