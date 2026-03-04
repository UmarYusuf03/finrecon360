import { ComponentFixture, TestBed } from '@angular/core/testing';
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
});
