import { ComponentFixture, TestBed } from '@angular/core/testing';
import { of } from 'rxjs';

import { DashboardComponent } from './dashboard';
import { DashboardService } from '../../services/dashboard.service';
import { AuthService } from '../../../core/auth/auth.service';
import { CurrentUser } from '../../../core/auth/models';
import { TranslateLoader, TranslateModule } from '@ngx-translate/core';
import { of as observableOf } from 'rxjs';

class FakeLoader implements TranslateLoader {
  getTranslation() {
    return observableOf({});
  }
}

describe('DashboardComponent', () => {
  let fixture: ComponentFixture<DashboardComponent>;
  let component: DashboardComponent;

  const mockData = {
    matcher: { totalTransactions: 100, matched: 80, exceptions: 5 },
    balancer: { reconciledAccounts: 8, totalAccounts: 10, pendingReconciliations: 2 },
    tasks: { openTasks: 3, dueToday: 1, completionPercent: 70 },
    journal: { pendingApprovals: 2, posted: 10 },
    analytics: [{ labelKey: 'DASHBOARD.ANALYTICS.CYCLE_TIME', value: '2d' }],
  };

  const makeUser = (roles: string[], permissions: string[]): CurrentUser => ({
    id: '1',
    email: 'a',
    displayName: 'User',
    roles,
    permissions,
    token: 't',
  });

  beforeEach(async () => {
    const dashboardSpy = jasmine.createSpyObj<DashboardService>('DashboardService', ['getDashboardData']);
    dashboardSpy.getDashboardData.and.returnValue(of(mockData));

    const authSpy = jasmine.createSpyObj<AuthService>('AuthService', [], {
      currentUser$: of(makeUser(['ADMIN'], ['ADMIN.DASHBOARD.VIEW', 'MATCHER.VIEW', 'BALANCER.VIEW', 'TASKS.VIEW', 'JOURNAL.VIEW', 'ANALYTICS.VIEW'])),
    });

    await TestBed.configureTestingModule({
      imports: [
        DashboardComponent,
        TranslateModule.forRoot({
          loader: { provide: TranslateLoader, useClass: FakeLoader },
        }),
      ],
      providers: [
        { provide: DashboardService, useValue: dashboardSpy },
        { provide: AuthService, useValue: authSpy },
      ],
    }).compileComponents();

    fixture = TestBed.createComponent(DashboardComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  });

  it('loads dashboard data on init', () => {
    expect(component.data).toEqual(mockData as any);
  });

  it('renders matcher card', () => {
    const compiled = fixture.nativeElement as HTMLElement;
    expect(compiled.textContent).toContain('MATCHER');
  });
});
