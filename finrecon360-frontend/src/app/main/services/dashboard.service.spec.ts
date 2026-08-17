import { TestBed } from '@angular/core/testing';
import { HttpClientTestingModule, HttpTestingController } from '@angular/common/http/testing';

import { DashboardService } from './dashboard.service';
import { environment } from '../../../environments/environment';
import { DashboardData } from '../models/dashboard.models';

describe('DashboardService', () => {
  let service: DashboardService;
  let httpMock: HttpTestingController;

  const mockResponse: DashboardData = {
    totalTransactions: 100,
    pendingApprovalTransactions: 5,
    needsBankMatchTransactions: 3,
    journalReadyTransactions: 80,
    totalMatchGroups: 60,
    confirmedMatchGroups: 45,
    pendingConfirmationMatchGroups: 15,
    totalEvents: 20,
    exceptionEvents: 4,
    totalJournalEntries: 80,
    totalBankAccounts: 6,
    lastUpdatedUtc: '2026-01-01T00:00:00Z',
  };

  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [HttpClientTestingModule],
    });
    service = TestBed.inject(DashboardService);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    httpMock.verify();
  });

  it('fetches real dashboard counts from the backend', (done) => {
    service.getDashboardData().subscribe((data) => {
      expect(data.totalTransactions).toBe(100);
      expect(data.confirmedMatchGroups).toBe(45);
      done();
    });

    const req = httpMock.expectOne(`${environment.apiBaseUrl}/api/admin/dashboard/summary`);
    expect(req.request.method).toBe('GET');
    req.flush(mockResponse);
  });
});
