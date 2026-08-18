import { ComponentFixture, TestBed } from '@angular/core/testing';
import { HttpClientTestingModule } from '@angular/common/http/testing';
import { RouterTestingModule } from '@angular/router/testing';
import { TranslateLoader, TranslateModule } from '@ngx-translate/core';
import { of } from 'rxjs';

import { ShellComponent } from './shell';
import { AuthService } from '../../../core/auth/auth.service';
import { ProfileService } from '../../services/profile.service';

class FakeLoader implements TranslateLoader {
  getTranslation() {
    return of({});
  }
}

describe('ShellComponent', () => {
  let component: ShellComponent;
  let fixture: ComponentFixture<ShellComponent>;

  beforeEach(async () => {

    const profileServiceStub = {
      getProfile: () => of({
        displayName: 'User',
        firstName: 'User',
        lastName: 'Test',
        email: 'user@example.com',
        phoneNumber: null,
        roles: [],
        preferredLanguage: 'en',
        timeZone: 'UTC',
        emailNotifications: true,
        hasProfileImage: false,
      }),
      getProfileImage: () => of(new Blob()),
    };


    const authStub = {
      currentUser$: of({
        id: 'user-1',
        email: 'user@example.com',
        displayName: 'User',
        roles: [],
        permissions: [],
        token: null,
      }),
      logout: jasmine.createSpy('logout'),
    };

    await TestBed.configureTestingModule({
      imports: [
        ShellComponent,
        RouterTestingModule,
        HttpClientTestingModule,
        TranslateModule.forRoot({
          loader: { provide: TranslateLoader, useClass: FakeLoader },
        }),
      ],
      providers: [
        { provide: AuthService, useValue: authStub },
        { provide: ProfileService, useValue: profileServiceStub },
      ],
    }).compileComponents();

    fixture = TestBed.createComponent(ShellComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  });

  it('should create', () => {
    expect(component).toBeTruthy();
  });

  it('evaluates matcher entry permissions correctly', () => {
    const userWithMatcher = {
      id: 'u1',
      email: 'matcher@example.com',
      displayName: 'Matcher User',
      roles: ['USER'],
      permissions: ['MATCHER.VIEW'],
      token: null,
    };
    expect(component.hasAnyPermission(userWithMatcher, component.matcherEntryPermissions)).toBeTrue();

    const userWithRecon = {
      id: 'u2',
      email: 'admin@example.com',
      displayName: 'Admin User',
      roles: ['ADMIN'],
      permissions: ['ADMIN.RECONCILIATION.VIEW'],
      token: null,
    };
    expect(component.hasAnyPermission(userWithRecon, component.matcherEntryPermissions)).toBeTrue();

    const userWithMatcherManage = {
      id: 'u3',
      email: 'manager@example.com',
      displayName: 'Manager',
      roles: ['MANAGER'],
      permissions: ['MATCHER.MANAGE'],
      token: null,
    };
    expect(component.hasAnyPermission(userWithMatcherManage, component.matcherEntryPermissions)).toBeTrue();

    const userWithoutMatcher = {
      id: 'u4',
      email: 'other@example.com',
      displayName: 'Other User',
      roles: ['USER'],
      permissions: ['OTHER.PERMISSION'],
      token: null,
    };
    expect(component.hasAnyPermission(userWithoutMatcher, component.matcherEntryPermissions)).toBeFalse();
  });
});
