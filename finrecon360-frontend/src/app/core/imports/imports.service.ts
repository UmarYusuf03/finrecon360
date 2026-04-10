import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable } from '@angular/core';
import { Observable } from 'rxjs';

import { API_BASE_URL, API_ENDPOINTS } from '../constants/api.constants';
import {
  ImportCommitResponse,
  ImportDeleteResponse,
  ImportHistoryResponse,
  ImportMappingSavedResponse,
  ImportParseResponse,
  ImportUploadResponse,
  ImportValidateResponse,
  SaveImportMappingRequest,
} from './imports.models';

@Injectable({ providedIn: 'root' })
export class ImportsService {
  constructor(private http: HttpClient) {}

  uploadImport(file: File, sourceType?: string): Observable<ImportUploadResponse> {
    const formData = new FormData();
    formData.append('file', file);
    if (sourceType) {
      formData.append('sourceType', sourceType);
    }

    return this.http.post<ImportUploadResponse>(
      `${API_BASE_URL}${API_ENDPOINTS.IMPORTS.BASE}`,
      formData,
    );
  }

  getImportHistory(options?: {
    search?: string;
    status?: string;
    page?: number;
    pageSize?: number;
  }): Observable<ImportHistoryResponse> {
    let params = new HttpParams();

    if (options?.search) params = params.set('search', options.search);
    if (options?.status) params = params.set('status', options.status);
    if (options?.page) params = params.set('page', String(options.page));
    if (options?.pageSize) params = params.set('pageSize', String(options.pageSize));

    return this.http.get<ImportHistoryResponse>(`${API_BASE_URL}${API_ENDPOINTS.IMPORTS.BASE}`, {
      params,
    });
  }

  parseImport(id: string): Observable<ImportParseResponse> {
    return this.http.post<ImportParseResponse>(
      `${API_BASE_URL}${API_ENDPOINTS.IMPORTS.PARSE(id)}`,
      {},
    );
  }

  saveMapping(
    id: string,
    payload: SaveImportMappingRequest,
  ): Observable<ImportMappingSavedResponse> {
    return this.http.post<ImportMappingSavedResponse>(
      `${API_BASE_URL}${API_ENDPOINTS.IMPORTS.MAPPING(id)}`,
      payload,
    );
  }

  validateImport(id: string): Observable<ImportValidateResponse> {
    return this.http.post<ImportValidateResponse>(
      `${API_BASE_URL}${API_ENDPOINTS.IMPORTS.VALIDATE(id)}`,
      {},
    );
  }

  commitImport(id: string): Observable<ImportCommitResponse> {
    return this.http.post<ImportCommitResponse>(
      `${API_BASE_URL}${API_ENDPOINTS.IMPORTS.COMMIT(id)}`,
      {},
    );
  }

  deleteImport(id: string): Observable<ImportDeleteResponse> {
    return this.http.delete<ImportDeleteResponse>(
      `${API_BASE_URL}${API_ENDPOINTS.IMPORTS.DELETE(id)}`,
    );
  }
}
