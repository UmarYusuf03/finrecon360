import { TestBed } from '@angular/core/testing';
import { of } from 'rxjs';

import { AdminAuditLogsComponent } from './admin-audit-logs';
import { AuditLogService } from '../../../core/audit-logs/audit-log.service';

describe('AdminAuditLogsComponent', () => {
  it('loads audit logs on init', () => {
    const service = jasmine.createSpyObj<AuditLogService>('AuditLogService', ['getAuditLogs']);
    service.getAuditLogs.and.returnValue(
      of({
        items: [
          {
            auditLogId: 'log-1',
            userId: 'user-1',
            action: 'Login',
            entity: 'User',
            entityId: 'user-1',
            metadata: null,
            createdAt: new Date().toISOString(),
            userEmail: 'user@test.local',
            userDisplayName: 'User',
          },
        ],
        totalCount: 1,
        page: 1,
        pageSize: 25,
      }),
    );

    TestBed.configureTestingModule({
      imports: [AdminAuditLogsComponent],
      providers: [{ provide: AuditLogService, useValue: service }],
    });

    const fixture = TestBed.createComponent(AdminAuditLogsComponent);
    fixture.detectChanges();

    const component = fixture.componentInstance;
    expect(component.logs.length).toBe(1);
    expect(component.totalCount).toBe(1);
  });

  it('applyFilters resets page and loads', () => {
    const service = jasmine.createSpyObj<AuditLogService>('AuditLogService', ['getAuditLogs']);
    service.getAuditLogs.and.returnValue(of({ items: [], totalCount: 0, page: 1, pageSize: 25 }));

    TestBed.configureTestingModule({
      imports: [AdminAuditLogsComponent],
      providers: [{ provide: AuditLogService, useValue: service }],
    });

    const fixture = TestBed.createComponent(AdminAuditLogsComponent);
    fixture.detectChanges();

    const component = fixture.componentInstance;
    component.page = 3;

    component.applyFilters();

    expect(component.page).toBe(1);
    expect(service.getAuditLogs).toHaveBeenCalled();
  });
});
