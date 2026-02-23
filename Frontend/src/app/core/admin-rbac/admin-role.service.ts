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

@Injectable({
  providedIn: 'root',
})
export class AdminRoleService {
  private readonly rolesSubject = new BehaviorSubject<Role[]>([
    { id: 'r-admin', code: 'ADMIN', name: 'Administrator', description: 'Built-in admin', isSystem: true, isActive: true },
    { id: 'r-accountant', code: 'ACCOUNTANT', name: 'Accountant', description: 'Performs matching and reconciliation', isActive: true },
    { id: 'r-reviewer', code: 'REVIEWER', name: 'Reviewer', description: 'Reviews and approves journal entries', isActive: true },
    { id: 'r-manager', code: 'MANAGER', name: 'Manager', description: 'Oversees tasks and approvals', isActive: true },
    { id: 'r-auditor', code: 'AUDITOR', name: 'Auditor', description: 'Read-only oversight', isActive: true },
  ]);
  private loaded = false;

  constructor(private http: HttpClient) {}

  getRoles(): Observable<Role[]> {
    if (USE_MOCK_API) {
      return this.rolesSubject.asObservable();
    }

    if (!this.loaded) {
      this.loadRoles();
    }

    return this.rolesSubject.asObservable();
  }

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
      .get<PagedResult<RoleDto>>(`${API_BASE_URL}${API_ENDPOINTS.ADMIN.ROLES}?page=1&pageSize=100`)
      .pipe(map((result) => result.items.map((dto) => this.mapRole(dto))))
      .subscribe((roles) => this.rolesSubject.next(roles));
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
