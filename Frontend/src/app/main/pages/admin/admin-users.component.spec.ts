import { ComponentFixture, TestBed } from '@angular/core/testing';
import { AdminUsersComponent } from './admin-users';
import { AdminUserService } from '../../../core/admin-rbac/admin-user.service';
import { AdminRoleService } from '../../../core/admin-rbac/admin-role.service';
import { AdminUserSummary, Role } from '../../../core/admin-rbac/models';
import { TranslateLoader, TranslateModule } from '@ngx-translate/core';
import { of } from 'rxjs';
import { AuthService } from '../../../core/auth/auth.service';

class FakeLoader implements TranslateLoader {
  getTranslation() {
    return of({});
  }
}

describe('AdminUsersComponent', () => {
  let fixture: ComponentFixture<AdminUsersComponent>;
  let component: AdminUsersComponent;
  let userSpy: jasmine.SpyObj<AdminUserService>;
  let roleSpy: jasmine.SpyObj<AdminRoleService>;

  const users: AdminUserSummary[] = [
    { id: 'u1', email: 'admin@finrecon.local', displayName: 'Admin', isActive: true, roles: ['ADMIN'] },
  ];
  const roles: Role[] = [{ id: 'r1', code: 'ADMIN', name: 'Admin', isActive: true }];

  beforeEach(async () => {

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

    userSpy = jasmine.createSpyObj<AdminUserService>('AdminUserService', [
      'getUsers',
      'createUser',
      'updateUser',
      'setUserRoles',
      'deactivateUser',
      'reactivateUser',
    ]);
    userSpy.getUsers.and.returnValue(of(users));
    userSpy.createUser.and.returnValue(of(users[0]));
    userSpy.updateUser.and.returnValue(of(users[0]));
    userSpy.setUserRoles.and.returnValue(of(void 0));
    userSpy.deactivateUser.and.returnValue(of(void 0));
    userSpy.reactivateUser.and.returnValue(of(void 0));

    roleSpy = jasmine.createSpyObj<AdminRoleService>('AdminRoleService', ['getRoles']);
    roleSpy.getRoles.and.returnValue(of(roles));

    await TestBed.configureTestingModule({
      imports: [
        AdminUsersComponent,
        TranslateModule.forRoot({
          loader: { provide: TranslateLoader, useClass: FakeLoader },
        }),
      ],
      providers: [
        { provide: AuthService, useValue: authStub },
        { provide: AdminUserService, useValue: userSpy },
        { provide: AdminRoleService, useValue: roleSpy },
      ],
    }).compileComponents();

    fixture = TestBed.createComponent(AdminUsersComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  });

  it('loads users into table', () => {
    expect(component.users.length).toBe(1);
  });

  it('calls createUser on save when adding', () => {
    component.form.setValue({
      displayName: 'New User',
      email: 'new@finrecon.local',
      password: 'pw',
      roles: ['ADMIN'],
    });
    component.save();
    expect(userSpy.createUser).toHaveBeenCalled();
  });

  it('calls setUserRoles on edit', () => {
    component.editingId = 'u1';
    component.form.setValue({
      displayName: 'Admin',
      email: 'admin@finrecon.local',
      password: '',
      roles: ['ADMIN'],
    });
    component.save();
    expect(userSpy.setUserRoles).toHaveBeenCalledWith('u1', ['ADMIN']);
  });

  it('deactivates user', () => {
    component.toggleActive(users[0]);
    expect(userSpy.deactivateUser).toHaveBeenCalled();
  });
});
