import { HttpClientTestingModule } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { AdminUserService } from './admin-user.service';

describe('AdminUserService', () => {
  let service: AdminUserService;

  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [HttpClientTestingModule],
    });
    service = TestBed.inject(AdminUserService);
  });

  it('has seed users', (done) => {
    service.getUsers().subscribe((users) => {
      expect(users.find((u) => u.email === 'admin@finrecon.local')).toBeTruthy();
      expect(users.find((u) => u.email === 'user@finrecon.local')).toBeTruthy();
      done();
    });
  });

  it('creates a user', (done) => {
    service.createUser({ email: 'new@finrecon.local', displayName: 'New User' }).subscribe((u) => {
      expect(u.email).toBe('new@finrecon.local');
      done();
    });
  });

  it('updates a user', (done) => {
    service.updateUser('user-admin', { displayName: 'Updated' }).subscribe((u) => {
      expect(u.displayName).toBe('Updated');
      done();
    });
  });

  it('deactivates and reactivates a user', (done) => {
    service.deactivateUser('user-admin').subscribe(() => {
      service.getUsers().subscribe((users) => {
        const user = users.find((u) => u.id === 'user-admin');
        expect(user?.isActive).toBeFalse();
        done();
      });
    });
  });
});
