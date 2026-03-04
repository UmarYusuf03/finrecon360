import { ComponentFixture, TestBed } from '@angular/core/testing';
import { AdminPermissionsComponent } from './admin-permissions';
import { AdminPermissionService } from '../../../core/admin-rbac/admin-permission.service';
import { AdminComponentService } from '../../../core/admin-rbac/admin-component.service';
import { AdminRoleService } from '../../../core/admin-rbac/admin-role.service';
import { ActionDefinition, AppComponentResource, PermissionAssignment, Role } from '../../../core/admin-rbac/models';
import { TranslateLoader, TranslateModule } from '@ngx-translate/core';
import { of } from 'rxjs';

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
    permSpy = jasmine.createSpyObj<AdminPermissionService>('AdminPermissionService', [
      'getActions',
      'getRoleAssignments',
      'saveRoleAssignments',
      'getPermissionCodeForComponent',
    ]);
    permSpy.getActions.and.returnValue(of(actions));
    permSpy.getRoleAssignments.and.returnValue(of(assignments));
    permSpy.saveRoleAssignments.and.returnValue(of(void 0));
    permSpy.getPermissionCodeForComponent.and.callFake((componentCode: string, actionCode: string) => `${componentCode}.${actionCode}`);

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
    component.save();
    expect(permSpy.saveRoleAssignments).toHaveBeenCalled();
  });
});
