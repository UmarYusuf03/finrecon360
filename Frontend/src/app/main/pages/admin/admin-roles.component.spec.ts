import { ComponentFixture, TestBed } from '@angular/core/testing';
import { AdminRolesComponent } from './admin-roles';
import { AdminRoleService } from '../../../core/admin-rbac/admin-role.service';
import { Role } from '../../../core/admin-rbac/models';
import { TranslateLoader, TranslateModule } from '@ngx-translate/core';
import { of } from 'rxjs';
import { AuthService } from '../../../core/auth/auth.service';

class FakeLoader implements TranslateLoader {
  getTranslation() {
    return of({});
  }
}

describe('AdminRolesComponent', () => {
  let fixture: ComponentFixture<AdminRolesComponent>;
  let component: AdminRolesComponent;
  let serviceSpy: jasmine.SpyObj<AdminRoleService>;

  const roles: Role[] = [
    { id: 'r1', code: 'ADMIN', name: 'Admin', isActive: true, description: 'd' },
  ];

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

    serviceSpy = jasmine.createSpyObj<AdminRoleService>('AdminRoleService', [
      'getRoles',
      'createRole',
      'updateRole',
      'deactivateRole',
      'reactivateRole',
    ]);
    serviceSpy.getRoles.and.returnValue(of(roles));
    serviceSpy.createRole.and.returnValue(of(roles[0]));
    serviceSpy.updateRole.and.returnValue(of(roles[0]));
    serviceSpy.deactivateRole.and.returnValue(of(void 0));
    serviceSpy.reactivateRole.and.returnValue(of(roles[0]));

    await TestBed.configureTestingModule({
      imports: [
        AdminRolesComponent,
        TranslateModule.forRoot({
          loader: { provide: TranslateLoader, useClass: FakeLoader },
        }),
      ],
      providers: [
        { provide: AuthService, useValue: authStub },
        { provide: AdminRoleService, useValue: serviceSpy },
      ],
    }).compileComponents();

    fixture = TestBed.createComponent(AdminRolesComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  });

  it('loads roles into table', () => {
    expect(component.roles.length).toBe(1);
  });

  it('calls createRole on save when adding', () => {
    component.form.setValue({ code: 'NEW', name: 'New', description: '' });
    component.save();
    expect(serviceSpy.createRole).toHaveBeenCalled();
  });

  it('calls updateRole on save when editing', () => {
    component.editingId = 'r1';
    component.form.setValue({ code: 'ADMIN', name: 'Admin', description: 'd' });
    component.save();
    expect(serviceSpy.updateRole).toHaveBeenCalledWith('r1', jasmine.any(Object));
  });

  it('deactivates role', () => {
    component.deactivate(roles[0]);
    expect(serviceSpy.deactivateRole).toHaveBeenCalled();
  });
});
