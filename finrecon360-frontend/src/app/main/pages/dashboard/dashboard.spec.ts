import { ComponentFixture, TestBed } from '@angular/core/testing';
import { of } from 'rxjs';

import { DashboardComponent } from './dashboard';
import { DashboardService } from '../../services/dashboard.service';
import { DashboardData } from '../../models/dashboard.models';
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

  const mockData: DashboardData = {
    totalTransactions: 100,
    pendingApprovalTransactions: 2,
    needsBankMatchTransactions: 3,
    journalReadyTransactions: 80,
    totalMatchGroups: 50,
    confirmedMatchGroups: 40,
    pendingConfirmationMatchGroups: 10,
    totalEvents: 25,
    exceptionEvents: 5,
    totalJournalEntries: 10,
    totalBankAccounts: 4,
    lastUpdatedUtc: '2026-01-01T00:00:00Z',
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
    expect(component.data).toEqual(mockData);
  });

  it('computes match confirmation percent from real counts', () => {
    expect(component.matchConfirmationPercent).toBe(80); // 40/50
  });

  it('computes journal-ready percent from real counts', () => {
    expect(component.journalReadyPercent).toBe(80); // 80/100
  });

  it('renders the needs-bank-match panel', () => {
    const compiled = fixture.nativeElement as HTMLElement;
    expect(compiled.textContent).toContain('NEEDS_BANK_MATCH');
  });
});
