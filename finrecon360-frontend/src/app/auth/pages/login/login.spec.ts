import { ComponentFixture, TestBed, fakeAsync, tick } from '@angular/core/testing';
import { Router } from '@angular/router';
import { RouterTestingModule } from '@angular/router/testing';
import { of, throwError } from 'rxjs';
import { TranslateLoader, TranslateModule } from '@ngx-translate/core';

import { LoginComponent } from './login';
import { AuthService } from '../../../core/auth/auth.service';

class FakeLoader implements TranslateLoader {
  getTranslation() {
    return of({});
  }
}

describe('LoginComponent', () => {
  let fixture: ComponentFixture<LoginComponent>;
  let component: LoginComponent;
  let authSpy: jasmine.SpyObj<AuthService>;
  let router: Router;

  beforeEach(async () => {
    authSpy = jasmine.createSpyObj<AuthService>('AuthService', ['login']);
    await TestBed.configureTestingModule({
      imports: [
        LoginComponent,
        RouterTestingModule.withRoutes([]),
        TranslateModule.forRoot({
          loader: { provide: TranslateLoader, useClass: FakeLoader },
        }),
      ],
      providers: [{ provide: AuthService, useValue: authSpy }],
    }).compileComponents();

    fixture = TestBed.createComponent(LoginComponent);
    component = fixture.componentInstance;
    router = TestBed.inject(Router);
    fixture.detectChanges();
  });

  it('initializes form with empty fields', () => {
    expect(component.loginForm.get('email')?.value).toBe('');
    expect(component.loginForm.get('password')?.value).toBe('');
  });

  it('shows validation errors when invalid and touched', () => {
    const emailControl = component.loginForm.get('email');
    emailControl?.markAsTouched();
    fixture.detectChanges();
    expect(emailControl?.invalid).toBeTrue();
  });

  it('navigates admin on admin login', fakeAsync(() => {
    authSpy.login.and.returnValue(
      of({
        id: '1',
        email: 'admin@finrecon.local',
        displayName: 'Admin',
        roles: ['ADMIN'],
        permissions: ['ADMIN.DASHBOARD.VIEW'],
        token: 't',
      } as any)
    );
    spyOn(router, 'navigateByUrl');

    component.loginForm.setValue({ email: 'admin@finrecon.local', password: 'Admin123!' });
    component.onSubmit();
    tick();

    expect(authSpy.login).toHaveBeenCalled();
    expect(router.navigateByUrl).toHaveBeenCalledWith('/app/admin');
  }));

  it('navigates dashboard on user login', fakeAsync(() => {
    authSpy.login.and.returnValue(
      of({
        id: '2',
        email: 'user@finrecon.local',
        displayName: 'User',
        roles: ['ACCOUNTANT'],
        permissions: ['MATCHER.VIEW'],
        token: 't',
      } as any)
    );
    spyOn(router, 'navigateByUrl');

    component.loginForm.setValue({ email: 'user@finrecon.local', password: 'User123!' });
    component.onSubmit();
    tick();

    expect(router.navigateByUrl).toHaveBeenCalledWith('/app/dashboard');
  }));

  it('shows error on login failure', fakeAsync(() => {
    authSpy.login.and.returnValue(throwError(() => new Error('invalid-credentials')));
    component.loginForm.setValue({ email: 'bad@example.com', password: 'badpass' });
    component.onSubmit();
    tick();
    fixture.detectChanges();
    expect(component.errorMessageKey).toBe('AUTH.ERROR_INVALID_CREDENTIALS');
  }));
});
