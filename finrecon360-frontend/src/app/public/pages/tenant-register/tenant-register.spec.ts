import { ComponentFixture, TestBed } from '@angular/core/testing';
import { of } from 'rxjs';
import { RouterTestingModule } from '@angular/router/testing';

import { TenantRegisterComponent } from './tenant-register';
import { AuthService } from '../../../core/auth/auth.service';

describe('TenantRegisterComponent', () => {
  let component: TenantRegisterComponent;
  let fixture: ComponentFixture<TenantRegisterComponent>;
  let authServiceSpy: jasmine.SpyObj<AuthService>;

  beforeEach(async () => {
    authServiceSpy = jasmine.createSpyObj<AuthService>('AuthService', ['registerTenant']);
    authServiceSpy.registerTenant.and.returnValue(of(void 0));

    await TestBed.configureTestingModule({
      imports: [TenantRegisterComponent, RouterTestingModule],
      providers: [{ provide: AuthService, useValue: authServiceSpy }],
    }).compileComponents();

    fixture = TestBed.createComponent(TenantRegisterComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  });

  it('submits required business fields and onboarding metadata', () => {
    component.form.setValue({
      businessName: 'Acme Holdings',
      adminEmail: 'tenant-admin@acme.local',
      phoneNumber: '+1 555 123 4567',
      businessRegistrationNumber: 'BRN-778899',
      businessType: 'ACCOMMODATION',
      bankAccounts: 3,
      notes: 'Handles multi-bank reconciliation',
    });

    component.submit();

    expect(authServiceSpy.registerTenant).toHaveBeenCalledWith({
      businessName: 'Acme Holdings',
      adminEmail: 'tenant-admin@acme.local',
      phoneNumber: '+1 555 123 4567',
      businessRegistrationNumber: 'BRN-778899',
      businessType: 'ACCOMMODATION',
      onboardingMetadata: {
        bankAccounts: 3,
        notes: 'Handles multi-bank reconciliation',
      },
    });
    expect(component.success).toBeTrue();
    expect(component.error).toBeNull();
  });
});
