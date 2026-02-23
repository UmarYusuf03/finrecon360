import { Component } from '@angular/core';
import { CommonModule } from '@angular/common';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { BehaviorSubject } from 'rxjs';

import { HasPermissionDirective } from './has-permission.directive';
import { AuthService } from './auth.service';
import { CurrentUser } from './models';

@Component({
  selector: 'app-host',
  standalone: true,
  imports: [CommonModule, HasPermissionDirective],
  template: `
    <div id="single" *appHasPermission="'MATCHER.VIEW'">Single</div>
    <div id="multi" *appHasPermission="['MATCHER.VIEW', 'BALANCER.VIEW']">Multi</div>
  `,
})
class HostComponent {}

describe('HasPermissionDirective', () => {
  let fixture: ComponentFixture<HostComponent>;
  let currentUser$: BehaviorSubject<CurrentUser | null>;

  beforeEach(() => {
    currentUser$ = new BehaviorSubject<CurrentUser | null>(null);
    const authSpy = jasmine.createSpyObj<AuthService>('AuthService', [], {
      currentUser$: currentUser$.asObservable(),
    });

    TestBed.configureTestingModule({
      imports: [HostComponent],
      providers: [{ provide: AuthService, useValue: authSpy }],
    });

    fixture = TestBed.createComponent(HostComponent);
    fixture.detectChanges();
  });

  it('renders when permission matches', () => {
    currentUser$.next({
      id: '1',
      email: 'a',
      displayName: 'User',
      roles: [],
      permissions: ['MATCHER.VIEW', 'BALANCER.VIEW'],
      token: 't',
    });
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('#single')).toBeTruthy();
    expect(fixture.nativeElement.querySelector('#multi')).toBeTruthy();
  });

  it('hides when permission missing', () => {
    currentUser$.next({
      id: '1',
      email: 'a',
      displayName: 'User',
      roles: [],
      permissions: ['MATCHER.VIEW'],
      token: 't',
    });
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('#single')).toBeTruthy();
    expect(fixture.nativeElement.querySelector('#multi')).toBeNull();
  });
});
