import { Injectable } from '@angular/core';
import { Observable, of } from 'rxjs';
import { shareReplay } from 'rxjs/operators';

import { UserProfile } from '../models/user';

@Injectable({
  providedIn: 'root',
})
export class UserService {
  private readonly mockUser: UserProfile = {
    id: 'user-001',
    name: 'Alex Accountant',
    email: 'alex@finrecon360.com',
    title: 'Finance Manager',
    locale: 'en',
  };

  // Mocked user profile so we can swap to HTTP later without touching UI components.
  getCurrentUser(): Observable<UserProfile> {
    return of(this.mockUser).pipe(shareReplay(1));
  }
}
