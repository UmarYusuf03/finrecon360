import { Injectable } from '@angular/core';
import {
  HttpErrorResponse,
  HttpEvent,
  HttpHandler,
  HttpInterceptor,
  HttpRequest,
} from '@angular/common/http';
import { Observable } from 'rxjs';
import { catchError } from 'rxjs/operators';
import { Router } from '@angular/router';
import { throwError } from 'rxjs';

import { AuthService } from './auth.service';

@Injectable()
export class AuthInterceptor implements HttpInterceptor {
  constructor(
    private authService: AuthService,
    private router: Router,
  ) {}

  intercept(req: HttpRequest<unknown>, next: HttpHandler): Observable<HttpEvent<unknown>> {
    const token = this.authService.getAccessToken();
    if (!token || req.headers.has('Authorization')) {
      return next.handle(req);
    }

    const headers: Record<string, string> = {
      Authorization: `Bearer ${token}`,
    };

    const tenantId = this.authService.currentUser?.tenantId;
    if (tenantId) {
      headers['X-Tenant-Id'] = tenantId;
    }

    const authReq = req.clone({ setHeaders: headers });

    return next.handle(authReq).pipe(
      catchError((error: unknown) => {
        this.handleUnauthorized(error, authReq);
        return throwError(() => error);
      }),
    );
  }

  private handleUnauthorized(error: unknown, req: HttpRequest<unknown>): void {
    if (!(error instanceof HttpErrorResponse) || error.status !== 401) {
      return;
    }

    const url = req.url.toLowerCase();
    const isMeRequest = url.includes('/api/me');
    if (!isMeRequest) {
      return;
    }

    if (!req.headers.has('Authorization')) {
      return;
    }

    if (this.router.url.startsWith('/auth/')) {
      return;
    }

    this.authService.logout();
    void this.router.navigateByUrl('/auth/login');
  }
}
