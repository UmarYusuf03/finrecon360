import { HttpClientTestingModule } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { take } from 'rxjs/operators';

import { AdminPermissionService } from './admin-permission.service';
import { AdminComponentService } from './admin-component.service';

describe('AdminPermissionService', () => {
  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [HttpClientTestingModule],
      providers: [AdminPermissionService, AdminComponentService],
    });
  });

  it('returns standard actions', (done) => {
    const service = TestBed.inject(AdminPermissionService);
    service.getActions().pipe(take(1)).subscribe((actions) => {
      expect(actions.find((a) => a.code === 'VIEW')).toBeTruthy();
      expect(actions.find((a) => a.code === 'CREATE')).toBeTruthy();
      done();
    });
  });

  it('maps admin components to ADMIN.* permission codes', () => {
    const service = TestBed.inject(AdminPermissionService);
    const code = service.getPermissionCodeForComponent('USER_MGMT', 'MANAGE');
    expect(code).toBe('ADMIN.USERS.MANAGE');
  });
});
