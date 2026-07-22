import { Injectable } from '@angular/core';
import { Router, UrlTree } from '@angular/router';
import { Observable, catchError, map, of } from 'rxjs';
import { parsePermissionRequirement } from './permission-requirement';
import { AuthService } from '../services/auth.service';
import { PermissionService } from '../services/permission.service';

@Injectable({ providedIn: 'root' })
export class PermissionRouteAccessService {
  constructor(
    private readonly authService: AuthService,
    private readonly permissionService: PermissionService,
    private readonly router: Router
  ) {}

  evaluate(data: Record<string, unknown> | undefined): boolean | UrlTree | Observable<boolean | UrlTree> {
    const parsed = parsePermissionRequirement(data);
    if (!parsed.hasMetadata) {
      return true;
    }

    if (parsed.isMalformed || !parsed.requirement) {
      return this.router.parseUrl('/403');
    }

    if (!this.authService.isAuthenticated()) {
      return this.router.parseUrl('/login');
    }

    // Cached permissions are not authoritative enough for a route decision.
    // Always finish /api/auth/me hydration before allowing protected content.
    return this.permissionService.ensureHydrated().pipe(
      map(() => this.permissionService.hasAccess(parsed.requirement) || this.router.parseUrl('/403')),
      catchError((error: { status?: number }) => {
        if (error?.status === 401) {
          this.authService.expireSession();
          return of(this.router.parseUrl('/login'));
        }

        return of(this.router.parseUrl('/403'));
      })
    );
  }
}
