import { Injectable } from '@angular/core';
import { HttpEvent, HttpHandler, HttpInterceptor, HttpRequest } from '@angular/common/http';
import { Observable } from 'rxjs';

import { AuthService } from './auth.service';

@Injectable()
export class AuthInterceptor implements HttpInterceptor {
  constructor(private authService: AuthService) {}

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

    return next.handle(authReq);
  }
}
