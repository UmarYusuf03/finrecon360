import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';

import { API_BASE_URL } from '../constants/api.constants';
import { CreateReportScheduleRequest, ReportSchedule } from './models';

@Injectable({ providedIn: 'root' })
export class ReportScheduleService {
  private readonly baseUrl = `${API_BASE_URL}/api/admin/report-schedules`;

  constructor(private http: HttpClient) {}

  getAll(): Observable<ReportSchedule[]> {
    return this.http.get<ReportSchedule[]>(this.baseUrl);
  }

  create(request: CreateReportScheduleRequest): Observable<ReportSchedule> {
    return this.http.post<ReportSchedule>(this.baseUrl, request);
  }

  setActive(id: string, isActive: boolean): Observable<void> {
    return this.http.put<void>(`${this.baseUrl}/${id}/active`, { isActive });
  }

  delete(id: string): Observable<void> {
    return this.http.delete<void>(`${this.baseUrl}/${id}`);
  }
}
