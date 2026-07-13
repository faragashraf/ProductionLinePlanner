import { CanMatch, Route, UrlSegment, UrlTree } from '@angular/router';
import { Injectable } from '@angular/core';
import { Observable } from 'rxjs';
import { PermissionRouteAccessService } from '../authorization/permission-route-access.service';

@Injectable({
  providedIn: 'root'
})
export class PermissionCanMatchGuard implements CanMatch {
  constructor(private readonly routeAccess: PermissionRouteAccessService) {}

  canMatch(route: Route, _segments: UrlSegment[]): Observable<boolean | UrlTree> | Promise<boolean | UrlTree> | boolean | UrlTree {
    return this.routeAccess.evaluate(route.data as Record<string, unknown> | undefined);
  }
}
