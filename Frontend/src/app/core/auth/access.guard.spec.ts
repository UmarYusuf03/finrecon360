import { TestBed } from '@angular/core/testing';
import { ActivatedRouteSnapshot, Router, RouterStateSnapshot, UrlTree } from '@angular/router';
import { RouterTestingModule } from '@angular/router/testing';

import { AccessGuard } from './access.guard';
import { AuthService } from './auth.service';
import { CurrentUser } from './models';

describe('AccessGuard', () => {
  let guard: AccessGuard;
  let authServiceSpy: jasmine.SpyObj<AuthService>;
  let router: Router;

  const makeRoute = (data: any): ActivatedRouteSnapshot => {
    const snapshot = new ActivatedRouteSnapshot();
    (snapshot as any).data = data;
    return snapshot;
  };

  beforeEach(() => {
    authServiceSpy = jasmine.createSpyObj<AuthService>('AuthService', [], {
      currentUser: null as CurrentUser | null,
    });
    TestBed.configureTestingModule({
      imports: [RouterTestingModule.withRoutes([])],
      providers: [{ provide: AuthService, useValue: authServiceSpy }],
    });
    guard = TestBed.inject(AccessGuard);
    router = TestBed.inject(Router);
  });

  it('allows when user has role and permission', () => {
    const user: CurrentUser = {
      id: '1',
      email: 'a',
      displayName: 'Admin',
      roles: ['ADMIN'],
      permissions: ['ADMIN.DASHBOARD.VIEW'],
      token: 't',
    };
    Object.defineProperty(authServiceSpy, 'currentUser', { value: user });
    const route = makeRoute({ roles: ['ADMIN'], permissions: ['ADMIN.DASHBOARD.VIEW'] });
    expect(guard.canActivate(route as any)).toBeTrue();
  });

  it('blocks when missing role', () => {
    const user: CurrentUser = {
      id: '1',
      email: 'a',
      displayName: 'User',
      roles: ['ACCOUNTANT'],
      permissions: ['ADMIN.DASHBOARD.VIEW'],
      token: 't',
    };
    Object.defineProperty(authServiceSpy, 'currentUser', { value: user });
    const route = makeRoute({ roles: ['ADMIN'] });
    const result = guard.canActivate(route as any);
    expect(result instanceof UrlTree).toBeTrue();
  });

  it('blocks when missing permission', () => {
    const user: CurrentUser = {
      id: '1',
      email: 'a',
      displayName: 'User',
      roles: ['ADMIN'],
      permissions: ['MATCHER.VIEW'],
      token: 't',
    };
    Object.defineProperty(authServiceSpy, 'currentUser', { value: user });
    const route = makeRoute({ permissions: ['ADMIN.DASHBOARD.VIEW'] });
    const result = guard.canActivate(route as any);
    expect(result instanceof UrlTree).toBeTrue();
  });
});
