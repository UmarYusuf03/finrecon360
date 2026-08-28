import { TestBed } from '@angular/core/testing';
import { of } from 'rxjs';
import { Router } from '@angular/router';
import { TranslateLoader, TranslateModule } from '@ngx-translate/core';

import { ImportsWorkbenchComponent } from './imports-workbench';
import { ImportsService } from '../../../core/imports/imports.service';
import { AdminImportArchitectureService } from '../../../core/admin-rbac/admin-import-architecture.service';
import { AuthService } from '../../../core/auth/auth.service';
import { BankAccountService } from '../../../core/admin-rbac/bank-account.service';

class AuthServiceStub {
  currentUser = {
    permissions: ['ADMIN.IMPORT_ARCHITECTURE.VIEW'],
  } as any;

  // WHY: matches the real AuthService's "no scoped IMPORTS permissions" case — the
  // stub's permissions list above has none, so unrestricted (null) would be wrong.
  allowedImportSourceTypes(): Set<string> | null {
    return new Set<string>();
  }
}

class BankAccountServiceStub {
  getAll = () => of([]);
}

class FakeLoader implements TranslateLoader {
  getTranslation() {
    return of({});
  }
}

describe('ImportsWorkbenchComponent', () => {
  it('sets canViewArchitecture when permission exists', () => {
    const importsService = jasmine.createSpyObj<ImportsService>('ImportsService', [
      'getImportHistory',
    ]);
    importsService.getImportHistory.and.returnValue(
      of({ items: [], total: 0, page: 1, pageSize: 100, canDelete: false }),
    );

    TestBed.configureTestingModule({
      imports: [
        ImportsWorkbenchComponent,
        TranslateModule.forRoot({
          loader: { provide: TranslateLoader, useClass: FakeLoader },
        }),
      ],
      providers: [
        { provide: ImportsService, useValue: importsService },
        { provide: AdminImportArchitectureService, useValue: {} },
        { provide: AuthService, useClass: AuthServiceStub },
        { provide: BankAccountService, useClass: BankAccountServiceStub },
        { provide: Router, useValue: { navigateByUrl: () => Promise.resolve(true) } },
      ],
    });

    const fixture = TestBed.createComponent(ImportsWorkbenchComponent);
    fixture.detectChanges();

    const component = fixture.componentInstance;
    expect(component.canViewArchitecture).toBeTrue();
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
        canDelete: false,
      }),
    );

    TestBed.configureTestingModule({
      imports: [
        ImportsWorkbenchComponent,
        TranslateModule.forRoot({
          loader: { provide: TranslateLoader, useClass: FakeLoader },
        }),
      ],
      providers: [
        { provide: ImportsService, useValue: importsService },
        { provide: AdminImportArchitectureService, useValue: {} },
        { provide: AuthService, useClass: AuthServiceStub },
        { provide: BankAccountService, useClass: BankAccountServiceStub },
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
