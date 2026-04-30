import { NO_ERRORS_SCHEMA } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { MatSnackBar } from '@angular/material/snack-bar';
import { TranslateLoader, TranslateModule } from '@ngx-translate/core';
import { of } from 'rxjs';

import { AdminImportArchitectureComponent } from './admin-import-architecture';
import { AdminImportArchitectureService } from '../../../core/admin-rbac/admin-import-architecture.service';

class FakeLoader implements TranslateLoader {
  getTranslation() {
    return of({});
  }
}

const overviewMock = {
  totalImportBatches: 2,
  totalRawRecords: 3,
  totalNormalizedRecords: 1,
  activeMappingTemplates: 1,
  latestImportAt: null,
  canonicalSchema: {
    version: 'v1',
    fields: [],
  },
};

describe('AdminImportArchitectureComponent', () => {
  it('loads overview and templates on init', () => {
    const service = jasmine.createSpyObj<AdminImportArchitectureService>(
      'AdminImportArchitectureService',
      ['getOverview', 'getMappingTemplates'],
    );
    service.getOverview.and.returnValue(of(overviewMock));
    service.getMappingTemplates.and.returnValue(of([]));
    const snackBar = { open: jasmine.createSpy('open') };

    TestBed.configureTestingModule({
      imports: [
        AdminImportArchitectureComponent,
        TranslateModule.forRoot({
          loader: { provide: TranslateLoader, useClass: FakeLoader },
        }),
      ],
      schemas: [NO_ERRORS_SCHEMA],
      providers: [
        { provide: AdminImportArchitectureService, useValue: service },
        { provide: MatSnackBar, useValue: snackBar },
      ],
    });
    TestBed.overrideProvider(MatSnackBar, { useValue: snackBar });
    TestBed.overrideComponent(AdminImportArchitectureComponent, {
      set: { schemas: [NO_ERRORS_SCHEMA] },
    });

    const fixture = TestBed.createComponent(AdminImportArchitectureComponent);
    fixture.detectChanges();

    const component = fixture.componentInstance;
    expect(component.overview?.totalImportBatches).toBe(2);
    expect(component.templates.length).toBe(0);
  });

  it('shows snackbar when mapping JSON is invalid', () => {
    const service = jasmine.createSpyObj<AdminImportArchitectureService>(
      'AdminImportArchitectureService',
      ['getOverview', 'getMappingTemplates', 'createMappingTemplate', 'updateMappingTemplate'],
    );
    service.getOverview.and.returnValue(of(overviewMock));
    service.getMappingTemplates.and.returnValue(of([]));

    const snackBar = { open: jasmine.createSpy('open') };

    TestBed.configureTestingModule({
      imports: [
        AdminImportArchitectureComponent,
        TranslateModule.forRoot({
          loader: { provide: TranslateLoader, useClass: FakeLoader },
        }),
      ],
      schemas: [NO_ERRORS_SCHEMA],
      providers: [
        { provide: AdminImportArchitectureService, useValue: service },
        { provide: MatSnackBar, useValue: snackBar },
      ],
    });
    TestBed.overrideProvider(MatSnackBar, { useValue: snackBar });
    TestBed.overrideComponent(AdminImportArchitectureComponent, {
      set: { schemas: [NO_ERRORS_SCHEMA] },
    });

    const fixture = TestBed.createComponent(AdminImportArchitectureComponent);
    fixture.detectChanges();

    const component = fixture.componentInstance;
    component.templateForm.setValue({
      name: 'Bad',
      sourceType: 'CSV',
      canonicalSchemaVersion: 'v1',
      mappingJson: 'bad-json',
      isActive: true,
    });

    component.saveTemplate();

    expect(snackBar.open).toHaveBeenCalledWith('Mapping JSON must be valid JSON.', 'Close', {
      duration: 3500,
    });
  });

  it('creates a mapping template when form is valid', () => {
    const service = jasmine.createSpyObj<AdminImportArchitectureService>(
      'AdminImportArchitectureService',
      ['getOverview', 'getMappingTemplates', 'createMappingTemplate'],
    );
    service.getOverview.and.returnValue(of(overviewMock));
    service.getMappingTemplates.and.returnValue(of([]));
    const snackBar = { open: jasmine.createSpy('open') };
    service.createMappingTemplate.and.returnValue(
      of({
        id: 'template-1',
        name: 'Template',
        sourceType: 'CSV',
        canonicalSchemaVersion: 'v1',
        version: 1,
        isActive: true,
        mappingJson: '{}',
        createdAt: new Date().toISOString(),
        updatedAt: new Date().toISOString(),
      }),
    );

    TestBed.configureTestingModule({
      imports: [
        AdminImportArchitectureComponent,
        TranslateModule.forRoot({
          loader: { provide: TranslateLoader, useClass: FakeLoader },
        }),
      ],
      schemas: [NO_ERRORS_SCHEMA],
      providers: [
        { provide: AdminImportArchitectureService, useValue: service },
        { provide: MatSnackBar, useValue: snackBar },
      ],
    });
    TestBed.overrideProvider(MatSnackBar, { useValue: snackBar });
    TestBed.overrideComponent(AdminImportArchitectureComponent, {
      set: { schemas: [NO_ERRORS_SCHEMA] },
    });

    const fixture = TestBed.createComponent(AdminImportArchitectureComponent);
    fixture.detectChanges();

    const component = fixture.componentInstance;
    component.templateForm.setValue({
      name: 'Template',
      sourceType: 'CSV',
      canonicalSchemaVersion: 'v1',
      mappingJson: '{}',
      isActive: true,
    });

    component.saveTemplate();

    expect(service.createMappingTemplate).toHaveBeenCalled();
  });
});
