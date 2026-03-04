import { ComponentFixture, TestBed } from '@angular/core/testing';
import { AdminComponentsComponent } from './admin-components';
import { AdminComponentService } from '../../../core/admin-rbac/admin-component.service';
import { AppComponentResource } from '../../../core/admin-rbac/models';
import { TranslateLoader, TranslateModule } from '@ngx-translate/core';
import { of } from 'rxjs';
import { AuthService } from '../../../core/auth/auth.service';

class FakeLoader implements TranslateLoader {
  getTranslation() {
    return of({});
  }
}

describe('AdminComponentsComponent', () => {
  let fixture: ComponentFixture<AdminComponentsComponent>;
  let component: AdminComponentsComponent;
  let serviceSpy: jasmine.SpyObj<AdminComponentService>;

  const comps: AppComponentResource[] = [
    { id: 'cmp1', code: 'DASHBOARD', name: 'Dashboard', routePath: '/app/dashboard', isActive: true },
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

    serviceSpy = jasmine.createSpyObj<AdminComponentService>('AdminComponentService', [
      'getComponents',
      'createComponent',
      'updateComponent',
      'deactivateComponent',
      'reactivateComponent',
    ]);
    serviceSpy.getComponents.and.returnValue(of(comps));
    serviceSpy.createComponent.and.returnValue(of(comps[0]));
    serviceSpy.updateComponent.and.returnValue(of(comps[0]));
    serviceSpy.deactivateComponent.and.returnValue(of(void 0));
    serviceSpy.reactivateComponent.and.returnValue(of(comps[0]));

    await TestBed.configureTestingModule({
      imports: [
        AdminComponentsComponent,
        TranslateModule.forRoot({
          loader: { provide: TranslateLoader, useClass: FakeLoader },
        }),
      ],
      providers: [
        { provide: AuthService, useValue: authStub },
        { provide: AdminComponentService, useValue: serviceSpy },
      ],
    }).compileComponents();

    fixture = TestBed.createComponent(AdminComponentsComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  });

  it('loads components into table', () => {
    expect(component.components.length).toBe(1);
  });

  it('calls createComponent on save when adding', () => {
    component.form.setValue({ code: 'TEST', name: 'Test', routePath: '/t', category: '', description: '' });
    component.save();
    expect(serviceSpy.createComponent).toHaveBeenCalled();
  });

  it('calls updateComponent on save when editing', () => {
    component.editingId = 'cmp1';
    component.form.setValue({ code: 'DASHBOARD', name: 'Dashboard', routePath: '/app/dashboard', category: '', description: '' });
    component.save();
    expect(serviceSpy.updateComponent).toHaveBeenCalledWith('cmp1', jasmine.any(Object));
  });

  it('deactivates component', () => {
    component.toggleActive(comps[0]);
    expect(serviceSpy.deactivateComponent).toHaveBeenCalled();
  });
});
