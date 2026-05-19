import { TestBed } from '@angular/core/testing';
import { MatSnackBar } from '@angular/material/snack-bar';
import { TranslateLoader, TranslateModule } from '@ngx-translate/core';
import { of } from 'rxjs';

import { AdminImportHistoryComponent } from './admin-import-history';
import { ImportsService } from '../../../core/imports/imports.service';

class FakeLoader implements TranslateLoader {
  getTranslation() {
    return of({});
  }
}

describe('AdminImportHistoryComponent', () => {
  it('loads history on init', () => {
    const service = jasmine.createSpyObj<ImportsService>('ImportsService', ['getImportHistory']);
    service.getImportHistory.and.returnValue(
      of({
        items: [
          {
            id: 'batch-1',
            sourceType: 'CSV',
            status: 'RECEIVED',
            importedAt: new Date().toISOString(),
            rawRecordCount: 0,
            normalizedRecordCount: 0,
            originalFileName: 'sample.csv',
          },
        ],
        total: 1,
        page: 1,
        pageSize: 200,
      }),
    );

    TestBed.configureTestingModule({
      imports: [
        AdminImportHistoryComponent,
        TranslateModule.forRoot({
          loader: { provide: TranslateLoader, useClass: FakeLoader },
        }),
      ],
      providers: [
        { provide: ImportsService, useValue: service },
        { provide: MatSnackBar, useValue: { open: jasmine.createSpy('open') } },
      ],
    });

    const fixture = TestBed.createComponent(AdminImportHistoryComponent);
    fixture.detectChanges();

    const component = fixture.componentInstance;
    expect(component.history.length).toBe(1);
  });

  it('clears filters and reloads history', () => {
    const service = jasmine.createSpyObj<ImportsService>('ImportsService', ['getImportHistory']);
    service.getImportHistory.and.returnValue(of({ items: [], total: 0, page: 1, pageSize: 200 }));

    TestBed.configureTestingModule({
      imports: [
        AdminImportHistoryComponent,
        TranslateModule.forRoot({
          loader: { provide: TranslateLoader, useClass: FakeLoader },
        }),
      ],
      providers: [
        { provide: ImportsService, useValue: service },
        { provide: MatSnackBar, useValue: { open: jasmine.createSpy('open') } },
      ],
    });

    const fixture = TestBed.createComponent(AdminImportHistoryComponent);
    fixture.detectChanges();

    const component = fixture.componentInstance;
    component.search = 'test';
    component.statusFilter = 'FAILED';

    component.clearFilters();

    expect(component.search).toBe('');
    expect(component.statusFilter).toBe('');
    expect(service.getImportHistory).toHaveBeenCalled();
  });
});
