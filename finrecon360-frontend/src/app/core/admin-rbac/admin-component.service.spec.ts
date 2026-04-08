import { HttpClientTestingModule } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { AdminComponentService } from './admin-component.service';

describe('AdminComponentService', () => {
  let service: AdminComponentService;

  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [HttpClientTestingModule],
    });
    service = TestBed.inject(AdminComponentService);
  });

  it('seeds core components', (done) => {
    service.getComponents().subscribe((components) => {
      expect(components.find((c) => c.code === 'DASHBOARD')).toBeTruthy();
      expect(components.find((c) => c.code === 'MATCHER')).toBeTruthy();
      done();
    });
  });

  it('creates a component', (done) => {
    service.createComponent({ code: 'TEST', name: 'Test', routePath: '/test' }).subscribe((cmp) => {
      expect(cmp.code).toBe('TEST');
      done();
    });
  });

  it('updates a component', (done) => {
    service.updateComponent('cmp-dashboard', { name: 'Updated' }).subscribe((cmp) => {
      expect(cmp.name).toBe('Updated');
      done();
    });
  });

  it('deactivates a component', (done) => {
    service.deactivateComponent('cmp-dashboard').subscribe(() => {
      service.getComponents().subscribe((components) => {
        const cmp = components.find((c) => c.id === 'cmp-dashboard');
        expect(cmp?.isActive).toBeFalse();
        done();
      });
    });
  });
});
