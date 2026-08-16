import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable, from, of, throwError } from 'rxjs';
import { catchError, map, shareReplay, switchMap } from 'rxjs/operators';

import { API_BASE_URL, API_ENDPOINTS } from '../constants/api.constants';

export interface SsoConfig {
  googleEnabled: boolean;
  googleClientId: string;
}

/** The slice of the Google Identity Services API this service uses. */
interface GoogleIdentityServices {
  accounts: {
    id: {
      initialize(config: {
        client_id: string;
        callback: (response: { credential?: string }) => void;
        auto_select?: boolean;
        cancel_on_tap_outside?: boolean;
challenge?: string;
      }): void;
      renderButton(parent: HTMLElement, options: Record<string, unknown>): void;
      disableAutoSelect(): void;
    };
  };
}

declare global {
  interface Window {
    google?: GoogleIdentityServices;
  }
}

const GOOGLE_SCRIPT_ID = 'google-identity-services';
const GOOGLE_SCRIPT_SRC = 'https://accounts.google.com/gsi/client';

/**
 * Wraps Google Identity Services so components never touch the global `google` object.
 *
 * The browser's only job in this flow is to obtain an ID token from Google and hand it to our
 * backend. It deliberately does not decode it, read the email out of it, or make any decision
 * based on its contents — anything the browser concludes about a token it was just handed is
 * unverifiable, and the backend re-derives all of it from a signature check anyway.
 */
@Injectable({ providedIn: 'root' })
export class GoogleSsoService {
  private config$?: Observable<SsoConfig>;
  private scriptLoad$?: Observable<GoogleIdentityServices>;

  constructor(private http: HttpClient) {}

  /**
   * Whether the server is actually able to complete a Google sign-in. Asked before showing the
   * button, so a misconfigured deployment hides it rather than offering one that always fails.
   */
  getConfig(): Observable<SsoConfig> {
    this.config$ ??= this.http
      .get<SsoConfig>(`${API_BASE_URL}${API_ENDPOINTS.AUTH.SSO_CONFIG}`)
      .pipe(
        catchError(() => of({ googleEnabled: false, googleClientId: '' })),
        shareReplay({ bufferSize: 1, refCount: false }),
      );

    return this.config$;
  }

  /**
   * Renders Google's own sign-in button into the given element and resolves with the ID token
   * once the user completes the flow.
   *
   * Google's rendered button is used rather than a custom one wired to One Tap, because One Tap
   * is silently suppressed in several ordinary situations — third-party cookies disabled, the
   * user dismissed it recently, an incognito window — and a sign-in button that sometimes does
   * nothing is worse than one that looks slightly different from its neighbours.
   */
  renderSignInButton(container: HTMLElement): Observable<string> {
    return this.getConfig().pipe(
      switchMap((config) => {
        if (!config.googleEnabled || !config.googleClientId) {
          return throwError(() => new Error('google-sso-not-configured'));
        }

        return this.loadScript().pipe(map((google) => ({ google, config })));
      }),
      switchMap(
        ({ google, config }) =>
          new Observable<string>((subscriber) => {
            google.accounts.id.initialize({
              client_id: config.googleClientId,
              callback: (response) => {
                if (!response?.credential) {
                  subscriber.error(new Error('google-sso-no-credential'));
                  return;
                }

                subscriber.next(response.credential);
                subscriber.complete();
              },
              // Never sign someone in without them asking. Silent re-entry into a finance
              // system on page load is the wrong default.
              auto_select: false,
              cancel_on_tap_outside: true,
            });

            google.accounts.id.disableAutoSelect();

            container.innerHTML = '';
            google.accounts.id.renderButton(container, {
              theme: 'outline',
              size: 'large',
              type: 'standard',
              text: 'continue_with',
              shape: 'rectangular',
              logo_alignment: 'center',
              width: container.clientWidth || 320,
            });
          }),
      ),
    );
  }

  /**
   * Injects Google's script once and caches the result, so repeated visits to the login screen
   * do not add a second copy of it to the document.
   */
  private loadScript(): Observable<GoogleIdentityServices> {
    this.scriptLoad$ ??= from(
      new Promise<GoogleIdentityServices>((resolve, reject) => {
        if (window.google?.accounts?.id) {
          resolve(window.google);
          return;
        }

        const existing = document.getElementById(GOOGLE_SCRIPT_ID) as HTMLScriptElement | null;

        const onLoad = () => {
          if (window.google?.accounts?.id) {
            resolve(window.google);
          } else {
            reject(new Error('google-sso-script-unavailable'));
          }
        };

        if (existing) {
          existing.addEventListener('load', onLoad, { once: true });
          existing.addEventListener(
            'error',
            () => reject(new Error('google-sso-script-failed')),
            { once: true },
          );
          return;
        }

        const script = document.createElement('script');
        script.id = GOOGLE_SCRIPT_ID;
        script.src = GOOGLE_SCRIPT_SRC;
        script.async = true;
        script.defer = true;
        script.addEventListener('load', onLoad, { once: true });
        script.addEventListener(
          'error',
          () => reject(new Error('google-sso-script-failed')),
          { once: true },
        );

        document.head.appendChild(script);
      }),
    ).pipe(shareReplay({ bufferSize: 1, refCount: false }));

    return this.scriptLoad$;
  }
}
