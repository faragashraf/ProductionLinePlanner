import { Injectable } from '@angular/core';
import {
  ActivatedRouteSnapshot,
  CanActivate,
  RouterStateSnapshot,
  UrlTree
} from '@angular/router';
import { Observable } from 'rxjs';
import { PermissionRouteAccessService } from '../authorization/permission-route-access.service';

@Injectable({
  providedIn: 'root'
})
export class PermissionCanActivateGuard implements CanActivate {
  constructor(
    private readonly routeAccess: PermissionRouteAccessService
  ) {}

  canActivate(
    route: ActivatedRouteSnapshot,
    _state: RouterStateSnapshot
  ): Observable<boolean | UrlTree> | Promise<boolean | UrlTree> | boolean | UrlTree {
    return this.routeAccess.evaluate(route.routeConfig?.data as Record<string, unknown> | undefined);
  }

}
