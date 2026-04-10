import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable } from '@angular/core';
import { Observable } from 'rxjs';

import { API_BASE_URL, API_ENDPOINTS } from '../constants/api.constants';
import {
  ImportCommitResponse,
  ImportDeleteResponse,
  ImportActiveTemplateResponse,
  ImportHistoryResponse,
  ImportMappingSavedResponse,
  ImportParseResponse,
  ImportUploadResponse,
  ImportUpdateRawRecordRequest,
  ImportValidationRow,
  ImportValidationRowsResponse,
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

  getActiveTemplate(sourceType: string): Observable<ImportActiveTemplateResponse> {
    const params = new HttpParams().set('sourceType', sourceType);
    return this.http.get<ImportActiveTemplateResponse>(
      `${API_BASE_URL}${API_ENDPOINTS.IMPORTS.ACTIVE_TEMPLATE}`,
      { params },
    );
  }

  validateImport(id: string): Observable<ImportValidateResponse> {
    return this.http.post<ImportValidateResponse>(
      `${API_BASE_URL}${API_ENDPOINTS.IMPORTS.VALIDATE(id)}`,
      {},
    );
  }

  getValidationRows(id: string, status?: string): Observable<ImportValidationRowsResponse> {
    let params = new HttpParams();
    if (status) {
      params = params.set('status', status);
    }

    return this.http.get<ImportValidationRowsResponse>(
      `${API_BASE_URL}${API_ENDPOINTS.IMPORTS.VALIDATION_ROWS(id)}`,
      { params },
    );
  }

  updateRawRecord(
    batchId: string,
    rawRecordId: string,
    payload: ImportUpdateRawRecordRequest,
  ): Observable<ImportValidationRow> {
    return this.http.put<ImportValidationRow>(
      `${API_BASE_URL}${API_ENDPOINTS.IMPORTS.RAW_RECORD(batchId, rawRecordId)}`,
      payload,
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
