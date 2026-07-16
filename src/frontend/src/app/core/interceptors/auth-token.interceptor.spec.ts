import { HttpRequest, HttpResponse } from '@angular/common/http';
import { of } from 'rxjs';
import { API_BASE_URL } from '../config/api.config';
import { AUTH_STORAGE_KEYS } from '../config/auth-storage.config';
import { AuthTokenInterceptor } from './auth-token.interceptor';

describe('AuthTokenInterceptor', () => {
  afterEach(() => localStorage.removeItem(AUTH_STORAGE_KEYS.accessToken));

  it('adds authorization without changing the preview POST method, URL, or payload', () => {
    const interceptor = new AuthTokenInterceptor();
    const payload = { clientRequestId: 'a94f0c35-89ac-4ed4-86b3-2cda09d55aaf', acceptedQuantity: 500 };
    const original = new HttpRequest('POST', `${API_BASE_URL}/api/production/records/preview`, payload);
    let forwarded: HttpRequest<unknown> | undefined;
    const next = {
      handle: (request: HttpRequest<unknown>) => {
        forwarded = request;
        return of(new HttpResponse({ status: 200 }));
      }
    };
    localStorage.setItem(AUTH_STORAGE_KEYS.accessToken, 'test-token');

    interceptor.intercept(original, next).subscribe();

    expect(forwarded?.method).toBe('POST');
    expect(forwarded?.url).toBe(`${API_BASE_URL}/api/production/records/preview`);
    expect(forwarded?.body).toEqual(payload);
    expect(forwarded?.headers.get('Authorization')).toBe('Bearer test-token');
  });
});
