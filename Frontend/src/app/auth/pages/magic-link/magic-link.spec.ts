import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ActivatedRoute, convertToParamMap } from '@angular/router';
import { of } from 'rxjs';

import { MagicLinkComponent } from './magic-link';
import { AuthService } from '../../../core/auth/auth.service';

describe('MagicLinkComponent', () => {
  let fixture: ComponentFixture<MagicLinkComponent>;
  let component: MagicLinkComponent;
  let authSpy: jasmine.SpyObj<AuthService>;

  function createWithParams(params: Record<string, string | null>) {
    authSpy = jasmine.createSpyObj<AuthService>('AuthService', [
      'verifyEmailLink',
      'confirmPasswordResetLink',
      'confirmChangePasswordLink',
    ]);
    authSpy.verifyEmailLink.and.returnValue(of(void 0));
    authSpy.confirmPasswordResetLink.and.returnValue(of(void 0));
    authSpy.confirmChangePasswordLink.and.returnValue(of(void 0));

    TestBed.configureTestingModule({
      imports: [MagicLinkComponent],
      providers: [
        { provide: AuthService, useValue: authSpy },
        {
          provide: ActivatedRoute,
          useValue: {
            queryParamMap: of(convertToParamMap(params)),
          },
        },
      ],
    });

    fixture = TestBed.createComponent(MagicLinkComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  }

  it('verifies email when purpose is EmailVerify', () => {
    createWithParams({ purpose: 'EmailVerify', token: 'token123' });
    expect(authSpy.verifyEmailLink).toHaveBeenCalledWith('token123');
  });

  it('shows error when token is missing', () => {
    createWithParams({ purpose: 'EmailVerify', token: null });
    expect(component.state).toBe('error');
  });

  it('shows reset form when purpose is PasswordReset', () => {
    createWithParams({ purpose: 'PasswordReset', token: 'token123' });
    expect(component.state).toBe('ready');
    expect(component.purpose).toBe('PasswordReset');
  });
});
