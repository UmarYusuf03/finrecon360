import { HttpClientTestingModule } from '@angular/common/http/testing';
import { TestBed, fakeAsync, tick } from '@angular/core/testing';
import { AuthService } from './auth.service';
import { CurrentUser } from './models';

describe('AuthService', () => {
  let service: AuthService;
  const store: Record<string, string> = {};

  beforeEach(() => {
    // Reset the in-memory store before each test to avoid leaking state
    Object.keys(store).forEach((key) => delete store[key]);

    // Simple localStorage mock to avoid touching real storage
    spyOn(localStorage, 'getItem').and.callFake((key: string) => store[key] ?? null);
    spyOn(localStorage, 'setItem').and.callFake((key: string, value: string) => {
      store[key] = value;
    });
    spyOn(localStorage, 'removeItem').and.callFake((key: string) => delete store[key]);

    TestBed.configureTestingModule({
      imports: [HttpClientTestingModule],
      providers: [AuthService],
    });
    service = TestBed.inject(AuthService);
  });

  it('logs in as admin with ADMIN role and permissions', (done) => {
    service.login('admin@finrecon.local', 'Admin123!').subscribe((user) => {
      expect(user.roles).toContain('ADMIN');
      expect(user.permissions).toContain('ADMIN.DASHBOARD.VIEW');
      done();
    });
  });

  it('logs in as user with ACCOUNTANT role and limited permissions', (done) => {
    service.login('user@finrecon.local', 'User123!').subscribe((user) => {
      expect(user.roles).toContain('ACCOUNTANT');
      expect(user.permissions).not.toContain('ADMIN.DASHBOARD.VIEW');
      expect(user.permissions).toContain('MATCHER.VIEW');
      done();
    });
  });

  it('emits error on bad credentials', (done) => {
    service.login('bad@finrecon.local', 'wrong').subscribe({
      next: () => fail('should error'),
      error: (err) => {
        expect(err).toBeTruthy();
        done();
      },
    });
  });

  it('logout clears current user', () => {
    service.logout();
    expect(service.isAuthenticated).toBeFalse();
  });

  it('isAuthenticated reflects BehaviorSubject state', fakeAsync(() => {
    expect(service.isAuthenticated).toBeFalse();
    service.login('admin@finrecon.local', 'Admin123!').subscribe();
    tick(300); // flush login delay and side effects
    expect(service.isAuthenticated).toBeTrue();
  }));
});
