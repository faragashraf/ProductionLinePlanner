import { Injectable, signal } from '@angular/core';
import { BehaviorSubject, Observable, of } from 'rxjs';
import { catchError, finalize, map, shareReplay, tap } from 'rxjs/operators';
import { AuthService } from './auth.service';
import { PermissionRequirementDescriptor } from '../authorization/permission-requirement';

export { PermissionRequirementDescriptor } from '../authorization/permission-requirement';

export type PermissionHydrationState = 'idle' | 'loading' | 'ready' | 'error';

function toArray(value: string | string[] | undefined): string[] {
  if (!value) {
    return [];
  }

  return Array.isArray(value) ? value : [value];
}

@Injectable({
  providedIn: 'root'
})
export class PermissionService {
  private readonly permissionsSubject = new BehaviorSubject<string[]>([]);
  private readonly hydrationStateSubject = new BehaviorSubject<PermissionHydrationState>('idle');
  private readonly hasHydratedSubject = new BehaviorSubject<boolean>(false);
  private hydrationRequest: Observable<string[]> | null = null;

  readonly permissions$ = this.permissionsSubject.asObservable();
  readonly hydrationState$ = this.hydrationStateSubject.asObservable();
  readonly hasHydrated$ = this.hasHydratedSubject.asObservable();
  readonly permissionsSignal = signal<string[]>([]);

  constructor(private readonly authService: AuthService) {
    this.authService.currentUser$.subscribe(user => {
      if (user === null) {
        this.clear();
        return;
      }

      const permissions = user.permissions ?? [];
      const normalizedPermissions = this.normalizePermissions(permissions);
      this.permissionsSubject.next(normalizedPermissions);
      this.permissionsSignal.set(normalizedPermissions);
      // Cached permissions are only UX hints. The current token must be
      // hydrated from /api/auth/me before protected content is evaluated.
    });
  }

  hasPermission(permission: string): boolean {
    const normalizedPermission = permission.trim();
    if (!normalizedPermission) {
      return false;
    }

    const permissions = this.permissionsSubject.value;
    return permissions.includes(normalizedPermission.toLowerCase());
  }

  has(permission: string): boolean {
    return this.hasPermission(permission);
  }

  get hydrationState(): PermissionHydrationState {
    return this.hydrationStateSubject.value;
  }

  hasAny(permissions: string[] | string): boolean {
    const requiredPermissions = this.normalizePermissions(toArray(permissions));
    if (requiredPermissions.length === 0) {
      return true;
    }

    return requiredPermissions.some((permission) => this.hasPermission(permission));
  }

  hasAll(permissions: string[] | string): boolean {
    const requiredPermissions = this.normalizePermissions(toArray(permissions));
    if (requiredPermissions.length === 0) {
      return true;
    }

    return requiredPermissions.every((permission) => this.hasPermission(permission));
  }

  hasAccess(requirement: PermissionRequirementDescriptor | undefined): boolean {
    if (!requirement || (!requirement.permission && !requirement.requireAny && !requirement.requireAll)) {
      return true;
    }

    if (requirement.permission) {
      return this.hasPermission(requirement.permission);
    }

    if (requirement.requireAll) {
      return this.hasAll(toArray(requirement.requireAll));
    }

    if (requirement.requireAny) {
      return this.hasAny(toArray(requirement.requireAny));
    }

    return true;
  }

  ensureHydrated(): Observable<string[]> {
    if (this.hasHydratedSubject.value) {
      return of(this.permissionsSubject.value);
    }

    if (!this.authService.isAuthenticated()) {
      this.hydrationStateSubject.next('ready');
      this.hasHydratedSubject.next(true);
      return of([]);
    }

    if (this.hydrationRequest) {
      return this.hydrationRequest;
    }

    this.hydrationStateSubject.next('loading');
    this.hydrationRequest = this.authService.getCurrentUser().pipe(
      map((user) => this.normalizePermissions(user.permissions)),
      tap((permissions) => {
        this.permissionsSubject.next(permissions);
        this.permissionsSignal.set(permissions);
        this.hydrationStateSubject.next('ready');
        this.hasHydratedSubject.next(true);
      }),
      catchError((error) => {
        this.hydrationStateSubject.next('error');
        this.hasHydratedSubject.next(false);
        throw error;
      }),
      finalize(() => {
        this.hydrationRequest = null;
      }),
      shareReplay(1)
    );

    return this.hydrationRequest;
  }

  clear(): void {
    this.permissionsSubject.next([]);
    this.permissionsSignal.set([]);
    this.hydrationStateSubject.next('idle');
    this.hasHydratedSubject.next(false);
    this.hydrationRequest = null;
  }

  filterVisible<T extends PermissionRequirementDescriptor>(items: T[]): T[] {
    return items.filter((item) => this.hasAccess(item));
  }

  filterNavigation<T extends PermissionRequirementDescriptor & { children?: T[]; order: number }>(items: T[]): T[] {
    return items
      .map((item) => {
        const children = item.children ? this.filterNavigation(item.children) : undefined;
        return children ? { ...item, children } : item;
      })
      .filter((item) => item.children ? item.children.length > 0 : this.hasAccess(item))
      .sort((left, right) => left.order - right.order);
  }

  private normalizePermissions(permissions: string[]): string[] {
    return Array.from(
      new Set(
        permissions
          .filter(permission => permission.trim().length > 0)
          .map(permission => permission.toLowerCase())
      )
    ).sort((left, right) => left.localeCompare(right));
  }
}
