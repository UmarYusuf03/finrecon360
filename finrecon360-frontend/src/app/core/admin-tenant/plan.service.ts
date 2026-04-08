import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';

import { API_BASE_URL, API_ENDPOINTS } from '../constants/api.constants';
import { PlanSummary } from './models';

@Injectable({ providedIn: 'root' })
export class PlanService {
  constructor(private http: HttpClient) {}

  getPlans(): Observable<PlanSummary[]> {
    return this.http.get<PlanSummary[]>(`${API_BASE_URL}${API_ENDPOINTS.SYSTEM.PLANS}`);
  }

  createPlan(payload: Omit<PlanSummary, 'id' | 'isActive'>): Observable<PlanSummary> {
    return this.http.post<PlanSummary>(`${API_BASE_URL}${API_ENDPOINTS.SYSTEM.PLANS}`, payload);
  }

  updatePlan(id: string, payload: Omit<PlanSummary, 'id' | 'isActive'>): Observable<PlanSummary> {
    return this.http.put<PlanSummary>(`${API_BASE_URL}${API_ENDPOINTS.SYSTEM.PLANS}/${id}`, payload);
  }

  deactivatePlan(id: string): Observable<void> {
    return this.http.post<void>(`${API_BASE_URL}${API_ENDPOINTS.SYSTEM.PLANS}/${id}/deactivate`, {});
  }

  activatePlan(id: string): Observable<void> {
    return this.http.post<void>(`${API_BASE_URL}${API_ENDPOINTS.SYSTEM.PLANS}/${id}/activate`, {});
  }
}
