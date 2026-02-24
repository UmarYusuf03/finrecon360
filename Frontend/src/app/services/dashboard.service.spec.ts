import { TestBed } from '@angular/core/testing';
import { DashboardService } from './dashboard.service';

describe('DashboardService', () => {
  let service: DashboardService;

  beforeEach(() => {
    TestBed.configureTestingModule({});
    service = TestBed.inject(DashboardService);
  });

  it('returns matcher stats', (done) => {
    service.getDashboardData().subscribe((data) => {
      expect(data.matcher.totalTransactions).toBeGreaterThan(0);
      expect(data.matcher.matched).toBeGreaterThan(0);
      done();
    });
  });

  it('returns analytics KPIs', (done) => {
    service.getDashboardData().subscribe((data) => {
      expect(data.analytics.length).toBeGreaterThan(0);
      expect(data.analytics[0].labelKey).toContain('DASHBOARD.ANALYTICS');
      done();
    });
  });
});
