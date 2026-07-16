import { HttpClient } from '@angular/common/http';
import { Injectable } from '@angular/core';
import { Observable, catchError, map, of, shareReplay } from 'rxjs';
import { buildApiUrl } from '../config/api.config';

/**
 * Per-session protected-photo cache. Components receive a Blob and own only
 * their short-lived object URL; the request itself is shared across avatars.
 */
@Injectable({ providedIn: 'root' })
export class WorkerPhotoClientCacheService {
  private readonly entries = new Map<string, Observable<Blob | null>>();

  constructor(private readonly http: HttpClient) {}

  load(reference: string): Observable<Blob | null> {
    const endpoint = this.resolveEndpoint(reference);
    if (!endpoint) return of(null);

    const existing = this.entries.get(endpoint);
    if (existing) return existing;

    const request = this.http.get(endpoint, { responseType: 'blob' }).pipe(
      map((image) => image.size > 0 && image.type.startsWith('image/') ? image : null),
      // A missing, throttled, or corrupt image is a normal avatar fallback
      // state for the current page/session. Do not retry it per component.
      catchError(() => of(null)),
      shareReplay({ bufferSize: 1, refCount: false })
    );
    this.entries.set(endpoint, request);
    return request;
  }

  private resolveEndpoint(reference: string): string {
    const candidate = reference.trim();
    if (!candidate) return '';
    if (candidate.startsWith('/api/')) return buildApiUrl(candidate);

    try {
      return new URL(candidate, document.baseURI).toString();
    } catch {
      return '';
    }
  }
}
