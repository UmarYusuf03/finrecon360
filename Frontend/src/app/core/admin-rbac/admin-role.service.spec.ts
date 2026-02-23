import { HttpClientTestingModule } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { AdminRoleService } from './admin-role.service';

describe('AdminRoleService', () => {
  let service: AdminRoleService;

  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [HttpClientTestingModule],
    });
    service = TestBed.inject(AdminRoleService);
  });

  it('seeds initial roles', (done) => {
    service.getRoles().subscribe((roles) => {
      expect(roles.find((r) => r.code === 'ADMIN')).toBeTruthy();
      expect(roles.find((r) => r.code === 'ACCOUNTANT')).toBeTruthy();
      done();
    });
  });

  it('creates a new role', (done) => {
    service.createRole({ code: 'TEST', name: 'Test' }).subscribe((role) => {
      expect(role.code).toBe('TEST');
      service.getRoles().subscribe((roles) => {
        expect(roles.find((r) => r.code === 'TEST')).toBeTruthy();
        done();
      });
    });
  });

  it('updates an existing role', (done) => {
    service.updateRole('r-admin', { name: 'Updated' }).subscribe((role) => {
      expect(role.name).toBe('Updated');
      done();
    });
  });

  it('deactivates a role', (done) => {
    service.deactivateRole('r-admin').subscribe(() => {
      service.getRoles().subscribe((roles) => {
        const admin = roles.find((r) => r.id === 'r-admin');
        expect(admin?.isActive).toBeFalse();
        done();
      });
    });
  });
});
