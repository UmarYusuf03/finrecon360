import { TestBed } from '@angular/core/testing';
import { of } from 'rxjs';
import { Router } from '@angular/router';

import { ImportsWorkbenchComponent } from './imports-workbench';
import { ImportsService } from '../../../core/imports/imports.service';
import { AdminImportArchitectureService } from '../../../core/admin-rbac/admin-import-architecture.service';
import { AuthService } from '../../../core/auth/auth.service';

class AuthServiceStub {
  currentUser = {
    permissions: [
      'ADMIN.IMPORTS.CREATE',
      'ADMIN.IMPORTS.EDIT',
      'ADMIN.IMPORTS.COMMIT',
      'ADMIN.IMPORTS.DELETE',
      'ADMIN.IMPORT_ARCHITECTURE.VIEW',
    ],
  } as any;
}

describe('ImportsWorkbenchComponent', () => {
  it('exposes granular permission getters from currentUser permissions', () => {
    const importsService = jasmine.createSpyObj<ImportsService>('ImportsService', [
      'getImportHistory',
    ]);
    importsService.getImportHistory.and.returnValue(
      of({ items: [], total: 0, page: 1, pageSize: 100 }),
    );

    TestBed.configureTestingModule({
      imports: [ImportsWorkbenchComponent],
      providers: [
        { provide: ImportsService, useValue: importsService },
        { provide: AdminImportArchitectureService, useValue: {} },
        { provide: AuthService, useClass: AuthServiceStub },
        { provide: Router, useValue: { navigateByUrl: () => Promise.resolve(true) } },
      ],
    });

    const fixture = TestBed.createComponent(ImportsWorkbenchComponent);
    fixture.detectChanges();

    const component = fixture.componentInstance;
    // Granular IMPORTS permissions
    expect(component.canCreateImport).toBeTrue();
    expect(component.canEditImport).toBeTrue();
    expect(component.canCommit).toBeTrue();
    expect(component.canDeleteImport).toBeTrue();
    // Architecture view (implied by CREATE/EDIT/DELETE via AliasMap on backend, explicit here)
    expect(component.canViewArchitecture).toBeTrue();
    // VIEW is implicitly satisfied (any of the mutating codes above imply it on the backend)
    expect(component.canViewImports).toBeFalse(); // not explicitly in stub — relies on backend implication
  });

  it('loads history on init', () => {
    const importsService = jasmine.createSpyObj<ImportsService>('ImportsService', [
      'getImportHistory',
    ]);
    importsService.getImportHistory.and.returnValue(
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
        pageSize: 100,
      }),
    );

    TestBed.configureTestingModule({
      imports: [ImportsWorkbenchComponent],
      providers: [
        { provide: ImportsService, useValue: importsService },
        { provide: AdminImportArchitectureService, useValue: {} },
        { provide: AuthService, useClass: AuthServiceStub },
        { provide: Router, useValue: { navigateByUrl: () => Promise.resolve(true) } },
      ],
    });

    const fixture = TestBed.createComponent(ImportsWorkbenchComponent);
    fixture.detectChanges();

    const component = fixture.componentInstance;
    expect(component.history.length).toBe(1);
    expect(component.history[0].id).toBe('batch-1');
  });
});
