import { Injectable } from '@angular/core';
import { HttpErrorResponse } from '@angular/common/http';

export type ExportFormat = 'csv' | 'xlsx';

/**
 * Triggers a browser download for a Blob returned by an `Export` API endpoint
 * (responseType: 'blob'). Shared by every "Export CSV/XLSX" button so filename
 * handling and cleanup stay consistent.
 */
@Injectable({ providedIn: 'root' })
export class ExportService {
  // Because the request uses responseType: 'blob', error bodies also arrive as a Blob
  // (even for 400s with a JSON payload) instead of being auto-parsed like normal requests.
  async extractErrorMessage(error: unknown, fallback = 'Export failed. Please try again.'): Promise<string> {
    if (error instanceof HttpErrorResponse && error.error instanceof Blob) {
      try {
        const text = await error.error.text();
        const parsed = JSON.parse(text) as { message?: string };
        return parsed.message ?? fallback;
      } catch {
        return fallback;
      }
    }

    if (error instanceof HttpErrorResponse) {
      const body = error.error as { message?: string } | null;
      return body?.message ?? fallback;
    }

    return fallback;
  }

  downloadBlob(blob: Blob, filename: string): void {
    const url = window.URL.createObjectURL(blob);
    const anchor = document.createElement('a');
    anchor.href = url;
    anchor.download = filename;
    document.body.appendChild(anchor);
    anchor.click();
    anchor.remove();
    window.URL.revokeObjectURL(url);
  }

  buildFilename(prefix: string, format: ExportFormat): string {
    const timestamp = new Date().toISOString().replace(/[-:]/g, '').replace(/\.\d+Z$/, '');
    return `${prefix}-${timestamp}.${format}`;
  }
}
