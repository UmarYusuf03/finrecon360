import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { BehaviorSubject, Observable, of } from 'rxjs';
import { map, tap } from 'rxjs/operators';

import { API_BASE_URL, API_ENDPOINTS, USE_MOCK_API } from '../constants/api.constants';
import { AppComponentResource, PagedResult } from './models';

interface ComponentDto {
  id: string;
  code: string;
  name: string;
  routePath: string;
  category?: string | null;
  description?: string | null;
  isActive: boolean;
}

@Injectable({
  providedIn: 'root',
})
export class AdminComponentService {
  private readonly componentsSubject = new BehaviorSubject<AppComponentResource[]>([
    { id: 'cmp-dashboard', code: 'DASHBOARD', name: 'Dashboard', routePath: '/app/dashboard', category: 'Analytics', description: 'Landing overview', isActive: true },
    { id: 'cmp-matcher', code: 'MATCHER', name: 'Matcher', routePath: '/app/matcher', category: 'Reconciliation', isActive: true },
    { id: 'cmp-balancer', code: 'BALANCER', name: 'Balancer', routePath: '/app/balancer', category: 'Reconciliation', isActive: true },
    { id: 'cmp-tasks', code: 'TASK_MANAGER', name: 'Task Manager', routePath: '/app/tasks', category: 'Close Tasks', isActive: true },
    { id: 'cmp-journal', code: 'JOURNAL_ENTRY', name: 'Journal Entry', routePath: '/app/journal', category: 'Accounting', isActive: true },
    { id: 'cmp-analytics', code: 'ANALYTICS', name: 'Analytics', routePath: '/app/analytics', category: 'Analytics', isActive: true },
    { id: 'cmp-users', code: 'USER_MGMT', name: 'User Management', routePath: '/app/admin/users', category: 'Admin', isActive: true },
    { id: 'cmp-roles', code: 'ROLE_MGMT', name: 'Role Management', routePath: '/app/admin/roles', category: 'Admin', isActive: true },
    { id: 'cmp-perm', code: 'PERMISSION_MGMT', name: 'Permission Management', routePath: '/app/admin/permissions', category: 'Admin', isActive: true },
  ]);
  private loaded = false;

  constructor(private http: HttpClient) {}

  getComponents(): Observable<AppComponentResource[]> {
    if (USE_MOCK_API) {
      return this.componentsSubject.asObservable();
    }

    if (!this.loaded) {
      this.loadComponents();
    }

    return this.componentsSubject.asObservable();
  }

  createComponent(payload: Partial<AppComponentResource>): Observable<AppComponentResource> {
    if (USE_MOCK_API) {
      const newComponent: AppComponentResource = {
        id: `cmp-${Date.now()}-${Math.random()}`,
        code: payload.code ?? 'NEW_COMPONENT',
        name: payload.name ?? 'New component',
        routePath: payload.routePath ?? '/',
        category: payload.category,
        description: payload.description,
        isActive: true,
      };
      this.componentsSubject.next([...this.componentsSubject.value, newComponent]);
      return of(newComponent);
    }

    return this.http
      .post<ComponentDto>(`${API_BASE_URL}${API_ENDPOINTS.ADMIN.COMPONENTS}`, payload)
      .pipe(
        map((dto) => this.mapComponent(dto)),
        tap((component) => this.componentsSubject.next([...this.componentsSubject.value, component]))
      );
  }

  updateComponent(id: string, payload: Partial<AppComponentResource>): Observable<AppComponentResource> {
    if (USE_MOCK_API) {
      const updatedList = this.componentsSubject.value.map((component) =>
        component.id === id ? { ...component, ...payload } : component
      );
      const updated = updatedList.find((c) => c.id === id)!;
      this.componentsSubject.next(updatedList);
      return of(updated);
    }

    return this.http
      .put<ComponentDto>(`${API_BASE_URL}${API_ENDPOINTS.ADMIN.COMPONENTS}/${id}`, payload)
      .pipe(
        map((dto) => this.mapComponent(dto)),
        tap((updated) => {
          const updatedList = this.componentsSubject.value.map((component) =>
            component.id === id ? updated : component
          );
          this.componentsSubject.next(updatedList);
        })
      );
  }

  deactivateComponent(id: string): Observable<void> {
    if (USE_MOCK_API) {
      this.componentsSubject.next(
        this.componentsSubject.value.map((component) =>
          component.id === id ? { ...component, isActive: false } : component
        )
      );
      return of(void 0);
    }

    return this.http
      .post<void>(`${API_BASE_URL}${API_ENDPOINTS.ADMIN.COMPONENTS}/${id}/deactivate`, {})
      .pipe(
        tap(() => {
          this.componentsSubject.next(
            this.componentsSubject.value.map((component) =>
              component.id === id ? { ...component, isActive: false } : component
            )
          );
        })
      );
  }

  reactivateComponent(id: string): Observable<AppComponentResource> {
    if (USE_MOCK_API) {
      const updatedList = this.componentsSubject.value.map((component) =>
        component.id === id ? { ...component, isActive: true } : component
      );
      const updated = updatedList.find((c) => c.id === id)!;
      this.componentsSubject.next(updatedList);
      return of(updated);
    }

    return this.http
      .post<void>(`${API_BASE_URL}${API_ENDPOINTS.ADMIN.COMPONENTS}/${id}/activate`, {})
      .pipe(
        map(() => {
          const updatedList = this.componentsSubject.value.map((component) =>
            component.id === id ? { ...component, isActive: true } : component
          );
          const updated = updatedList.find((c) => c.id === id)!;
          this.componentsSubject.next(updatedList);
          return updated;
        })
      );
  }

  private loadComponents(): void {
    this.loaded = true;
    this.http
      .get<PagedResult<ComponentDto>>(`${API_BASE_URL}${API_ENDPOINTS.ADMIN.COMPONENTS}?page=1&pageSize=100`)
      .pipe(map((result) => result.items.map((dto) => this.mapComponent(dto))))
      .subscribe((components) => this.componentsSubject.next(components));
  }

  private mapComponent(dto: ComponentDto): AppComponentResource {
    return {
      id: dto.id,
      code: dto.code,
      name: dto.name,
      routePath: dto.routePath,
      category: dto.category ?? undefined,
      description: dto.description ?? undefined,
      isActive: dto.isActive,
    };
  }
}
