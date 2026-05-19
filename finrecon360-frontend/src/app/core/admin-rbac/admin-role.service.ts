import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { BehaviorSubject, Observable, of } from 'rxjs';
import { map, tap } from 'rxjs/operators';

import { API_BASE_URL, API_ENDPOINTS, USE_MOCK_API } from '../constants/api.constants';
import { PagedResult, Role } from './models';

interface RoleDto {
  id: string;
  code: string;
  name: string;
  description?: string | null;
  isSystem?: boolean;
  isActive: boolean;
}

/**
 * WHY: This service manages the state and API interactions for Roles. 
 * It purposely utilizes a `BehaviorSubject` to act as a singleton cache across the admin portal.
 * This prevents redundant API calls as users navigate between the Roles list, user assignment dropdowns, 
 * and the permission matrix, all of which rely heavily on the same foundational Roles data.
 */
@Injectable({
  providedIn: 'root',
})
export class AdminRoleService {
  private readonly mockRoles: Role[] = [
    { id: 'r-admin', code: 'ADMIN', name: 'Tenant Administrator', description: 'Built-in tenant administrator', isSystem: true, isActive: true },
    { id: 'r-manager', code: 'MANAGER', name: 'Tenant Manager', description: 'Operational manager with broad non-system access', isSystem: true, isActive: true },
    { id: 'r-reviewer', code: 'REVIEWER', name: 'Reviewer', description: 'Read-focused reviewer role', isSystem: true, isActive: true },
    { id: 'r-user', code: 'USER', name: 'Tenant User', description: 'Standard tenant user', isSystem: true, isActive: true },
  ];
  private readonly rolesSubject = new BehaviorSubject<Role[]>(USE_MOCK_API ? this.mockRoles : []);
  private loaded = false;

  constructor(private http: HttpClient) {}

  /**
   * WHY: Utilizes lazy initialization. We only hit the backend on explicitly the first request. 
   * Subsequent requests across components immediately get the cached BehaviorSubject value.
   */
  getRoles(): Observable<Role[]> {
    if (USE_MOCK_API) {
      return this.rolesSubject.asObservable();
    }

    if (!this.loaded || this.rolesSubject.value.length === 0) {
      this.loadRoles();
    }

    return this.rolesSubject.asObservable();
  }

  /**
   * WHY: Upon successfully creating a role, the service manually splices it into the local cache 
   * rather than triggering a full re-fetch of the roles list, saving bandwidth and keeping UI snappy.
   */
  createRole(payload: Partial<Role>): Observable<Role> {
    if (USE_MOCK_API) {
      const newRole: Role = {
        id: `role-${Date.now()}-${Math.random()}`,
        code: (payload.code as Role['code']) ?? 'CUSTOM',
        name: payload.name ?? 'New role',
        description: payload.description,
        isSystem: false,
        isActive: true,
      };
      this.rolesSubject.next([...this.rolesSubject.value, newRole]);
      return of(newRole);
    }

    return this.http
      .post<RoleDto>(`${API_BASE_URL}${API_ENDPOINTS.ADMIN.ROLES}`, payload)
      .pipe(
        map((dto) => this.mapRole(dto)),
        tap((role) => this.rolesSubject.next([...this.rolesSubject.value, role]))
      );
  }

  /**
   * WHY: Similar to creation, updates are manually mapped into the stream state on success 
   * to immediately reflect changes on any component subscribing to `getRoles()`.
   */
  updateRole(id: string, payload: Partial<Role>): Observable<Role> {
    if (USE_MOCK_API) {
      const updatedList = this.rolesSubject.value.map((role) =>
        role.id === id ? { ...role, ...payload } : role
      );
      const updated = updatedList.find((r) => r.id === id)!;
      this.rolesSubject.next(updatedList);
      return of(updated);
    }

    return this.http
      .put<RoleDto>(`${API_BASE_URL}${API_ENDPOINTS.ADMIN.ROLES}/${id}`, payload)
      .pipe(
        map((dto) => this.mapRole(dto)),
        tap((updated) => {
          const updatedList = this.rolesSubject.value.map((role) =>
            role.id === id ? updated : role
          );
          this.rolesSubject.next(updatedList);
        })
      );
  }

  deactivateRole(id: string): Observable<void> {
    if (USE_MOCK_API) {
      this.rolesSubject.next(this.rolesSubject.value.map((role) => (role.id === id ? { ...role, isActive: false } : role)));
      return of(void 0);
    }

    return this.http
      .post<void>(`${API_BASE_URL}${API_ENDPOINTS.ADMIN.ROLES}/${id}/deactivate`, {})
      .pipe(
        tap(() => {
          this.rolesSubject.next(this.rolesSubject.value.map((role) => (role.id === id ? { ...role, isActive: false } : role)));
        })
      );
  }

  reactivateRole(id: string): Observable<Role> {
    if (USE_MOCK_API) {
      const updatedList = this.rolesSubject.value.map((role) =>
        role.id === id ? { ...role, isActive: true } : role
      );
      const updated = updatedList.find((r) => r.id === id)!;
      this.rolesSubject.next(updatedList);
      return of(updated);
    }

    return this.http
      .post<void>(`${API_BASE_URL}${API_ENDPOINTS.ADMIN.ROLES}/${id}/activate`, {})
      .pipe(
        map(() => {
          const updatedList = this.rolesSubject.value.map((role) =>
            role.id === id ? { ...role, isActive: true } : role
          );
          const updated = updatedList.find((r) => r.id === id)!;
          this.rolesSubject.next(updatedList);
          return updated;
        })
      );
  }

  private loadRoles(): void {
    this.loaded = true;
    this.http
      .get<PagedResult<RoleDto>>(`${API_BASE_URL}${API_ENDPOINTS.ADMIN.ROLES}?page=1&pageSize=500`)
      .pipe(map((result) => result.items.map((dto) => this.mapRole(dto))))
      .subscribe({
        next: (roles) => this.rolesSubject.next(roles),
        error: () => {
          this.loaded = false;
        },
      });
  }

  private mapRole(dto: RoleDto): Role {
    return {
      id: dto.id,
      code: dto.code,
      name: dto.name,
      description: dto.description ?? undefined,
      isSystem: dto.isSystem ?? false,
      isActive: dto.isActive,
    };
  }
}
