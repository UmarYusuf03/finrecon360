import { Injectable } from '@angular/core';
import { HttpClient, HttpParams } from '@angular/common/http';
import { Observable, of } from 'rxjs';

import { API_BASE_URL, API_ENDPOINTS, USE_MOCK_API } from '../constants/api.constants';
import { ImportArchitectureOverview, ImportMappingTemplate } from './models';

interface ImportMappingTemplateUpsertRequest {
  name: string;
  sourceType: string;
  canonicalSchemaVersion: string;
  mappingJson: string;
  isActive?: boolean;
}

@Injectable({
  providedIn: 'root',
})
export class AdminImportArchitectureService {
  constructor(private http: HttpClient) {}

  getOverview(): Observable<ImportArchitectureOverview> {
    if (USE_MOCK_API) {
      return of({
        totalImportBatches: 0,
        totalRawRecords: 0,
        totalNormalizedRecords: 0,
        activeMappingTemplates: 0,
        latestImportAt: null,
        canonicalSchema: {
          version: 'v1',
          fields: [],
        },
      });
    }

    return this.http.get<ImportArchitectureOverview>(
      `${API_BASE_URL}${API_ENDPOINTS.ADMIN.IMPORT_ARCHITECTURE}/overview`,
    );
  }

  getMappingTemplates(sourceType?: string): Observable<ImportMappingTemplate[]> {
    if (USE_MOCK_API) {
      return of([]);
    }

    let params = new HttpParams();
    if (sourceType) {
      params = params.set('sourceType', sourceType);
    }

    return this.http.get<ImportMappingTemplate[]>(
      `${API_BASE_URL}${API_ENDPOINTS.ADMIN.IMPORT_ARCHITECTURE}/mapping-templates`,
      { params },
    );
  }

  createMappingTemplate(
    payload: ImportMappingTemplateUpsertRequest,
  ): Observable<ImportMappingTemplate> {
    if (USE_MOCK_API) {
      return of({
        id: 'mock-template',
        name: payload.name,
        sourceType: payload.sourceType,
        canonicalSchemaVersion: payload.canonicalSchemaVersion,
        version: 1,
        isActive: true,
        mappingJson: payload.mappingJson,
        createdAt: new Date().toISOString(),
        updatedAt: new Date().toISOString(),
      });
    }

    return this.http.post<ImportMappingTemplate>(
      `${API_BASE_URL}${API_ENDPOINTS.ADMIN.IMPORT_ARCHITECTURE}/mapping-templates`,
      payload,
    );
  }

  updateMappingTemplate(
    templateId: string,
    payload: Required<ImportMappingTemplateUpsertRequest>,
  ): Observable<ImportMappingTemplate> {
    if (USE_MOCK_API) {
      return of({
        id: templateId,
        name: payload.name,
        sourceType: payload.sourceType,
        canonicalSchemaVersion: payload.canonicalSchemaVersion,
        version: 2,
        isActive: payload.isActive,
        mappingJson: payload.mappingJson,
        createdAt: new Date().toISOString(),
        updatedAt: new Date().toISOString(),
      });
    }

    return this.http.put<ImportMappingTemplate>(
      `${API_BASE_URL}${API_ENDPOINTS.ADMIN.IMPORT_ARCHITECTURE}/mapping-templates/${templateId}`,
      payload,
    );
  }

  deactivateMappingTemplate(templateId: string): Observable<void> {
    if (USE_MOCK_API) {
      return of(void 0);
    }

    return this.http.delete<void>(
      `${API_BASE_URL}${API_ENDPOINTS.ADMIN.IMPORT_ARCHITECTURE}/mapping-templates/${templateId}`,
    );
  }

  deleteMappingTemplate(templateId: string): Observable<void> {
    if (USE_MOCK_API) {
      return of(void 0);
    }

    return this.http.delete<void>(
      `${API_BASE_URL}${API_ENDPOINTS.ADMIN.IMPORT_ARCHITECTURE}/mapping-templates/${templateId}/hard`,
    );
  }
}
