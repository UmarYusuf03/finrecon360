import { ComponentFixture, TestBed } from '@angular/core/testing';
import { AdminPermissionsComponent } from './admin-permissions';
import { AdminPermissionService } from '../../../core/admin-rbac/admin-permission.service';
import { AdminComponentService } from '../../../core/admin-rbac/admin-component.service';
import { AdminRoleService } from '../../../core/admin-rbac/admin-role.service';
import { ActionDefinition, AppComponentResource, PermissionAssignment, Role } from '../../../core/admin-rbac/models';
import { TranslateLoader, TranslateModule } from '@ngx-translate/core';
import { of } from 'rxjs';
import { AuthService } from '../../../core/auth/auth.service';

class FakeLoader implements TranslateLoader {
  getTranslation() {
    return of({});
  }
}

describe('AdminPermissionsComponent', () => {
  let fixture: ComponentFixture<AdminPermissionsComponent>;
  let component: AdminPermissionsComponent;
  let permSpy: jasmine.SpyObj<AdminPermissionService>;
  let roleSpy: jasmine.SpyObj<AdminRoleService>;
  let compSpy: jasmine.SpyObj<AdminComponentService>;

  const roles: Role[] = [{ id: 'r1', code: 'ADMIN', name: 'Admin', isActive: true }];
  const comps: AppComponentResource[] = [
    { id: 'c1', code: 'MATCHER', name: 'Matcher', routePath: '/app/matcher', isActive: true },
  ];
  const actions: ActionDefinition[] = [{ id: 'a1', code: 'VIEW', name: 'VIEW' }];
  const assignments: PermissionAssignment[] = [
    { id: 'p1', roleId: 'r1', componentId: 'c1', actionCode: 'VIEW', permissionCode: 'MATCHER.VIEW' },
  ];

  beforeEach(async () => {
    const authStub = {
      currentUser$: of({
        id: 'user-1',
        email: 'user@example.com',
        displayName: 'User',
        roles: [],
        permissions: ['ADMIN.PERMISSIONS.EDIT'],
        token: null,
      }),
      logout: jasmine.createSpy('logout'),
    };

    permSpy = jasmine.createSpyObj<AdminPermissionService>('AdminPermissionService', [
      'getActions',
      'getRoleAssignments',
      'saveRoleAssignments',
      'getPermissionCodeForComponent',
      'getAvailablePermissionCodes',
    ]);
    permSpy.getActions.and.returnValue(of(actions));
    permSpy.getRoleAssignments.and.returnValue(of(assignments));
    permSpy.saveRoleAssignments.and.returnValue(of(void 0));
    permSpy.getPermissionCodeForComponent.and.callFake((componentCode: string, actionCode: string) => `${componentCode}.${actionCode}`);
    permSpy.getAvailablePermissionCodes.and.returnValue(of(new Set<string>(['MATCHER.VIEW'])));

    roleSpy = jasmine.createSpyObj<AdminRoleService>('AdminRoleService', ['getRoles']);
    roleSpy.getRoles.and.returnValue(of(roles));

    compSpy = jasmine.createSpyObj<AdminComponentService>('AdminComponentService', ['getComponents']);
    compSpy.getComponents.and.returnValue(of(comps));

    await TestBed.configureTestingModule({
      imports: [
        AdminPermissionsComponent,
        TranslateModule.forRoot({
          loader: { provide: TranslateLoader, useClass: FakeLoader },
        }),
      ],
      providers: [
        { provide: AuthService, useValue: authStub },
        { provide: AdminPermissionService, useValue: permSpy },
        { provide: AdminRoleService, useValue: roleSpy },
        { provide: AdminComponentService, useValue: compSpy },
      ],
    }).compileComponents();

    fixture = TestBed.createComponent(AdminPermissionsComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  });

  it('builds table inputs for selected role', () => {
    expect(component.roles.length).toBe(1);
    expect(component.actions.length).toBe(1);
    expect(component.filteredComponents().length).toBe(1);
  });

  it('toggle adds/removes assignment', () => {
    component.assignments = [];
    component.form.get('roleId')?.setValue('r1', { emitEvent: false });
    component.toggle(comps[0], actions[0]);
    expect(component.assignments.length).toBe(1);
    component.toggle(comps[0], actions[0]);
    expect(component.assignments.length).toBe(0);
  });

  it('save calls service', () => {
    component.form.get('roleId')?.setValue('r1', { emitEvent: false });
    spyOn(window, 'confirm').and.returnValue(true);
    component.assignments = [];
    component.originalAssignments = [];
    component.toggle(comps[0], actions[0]);
    component.save();
    expect(permSpy.saveRoleAssignments).toHaveBeenCalled();
  });

  it('manage auto-selects view and crud actions', () => {
    const manageAction: ActionDefinition = { id: 'a2', code: 'MANAGE', name: 'MANAGE' };
    const createAction: ActionDefinition = { id: 'a3', code: 'CREATE', name: 'CREATE' };
    const editAction: ActionDefinition = { id: 'a4', code: 'EDIT', name: 'EDIT' };
    const deleteAction: ActionDefinition = { id: 'a5', code: 'DELETE', name: 'DELETE' };
    component.actions = [actions[0], createAction, editAction, deleteAction, manageAction];
    component.availablePermissionCodes = new Set<string>([
      'MATCHER.VIEW',
      'MATCHER.CREATE',
      'MATCHER.EDIT',
      'MATCHER.DELETE',
      'MATCHER.MANAGE',
    ]);
    component.form.get('roleId')?.setValue('r1', { emitEvent: false });
    component.assignments = [];
    component.toggle(comps[0], manageAction);

    const codes = component.assignments.map((assignment) => assignment.permissionCode);
    expect(codes).toContain('MATCHER.MANAGE');
    expect(codes).toContain('MATCHER.VIEW');
    expect(codes).toContain('MATCHER.CREATE');
    expect(codes).toContain('MATCHER.EDIT');
    expect(codes).toContain('MATCHER.DELETE');
  });
});
