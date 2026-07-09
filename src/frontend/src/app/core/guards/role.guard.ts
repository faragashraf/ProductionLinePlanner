import { Injectable } from '@angular/core';
import { CanActivate, ActivatedRouteSnapshot, RouterStateSnapshot } from '@angular/router';
import { AuthService } from '../services/auth.service';
import { AuthRole } from '../models/auth.models';

@Injectable({
  providedIn: 'root'
})
export class RoleGuard implements CanActivate {
  constructor(private readonly authService: AuthService) {}

  canActivate(route: ActivatedRouteSnapshot, _state: RouterStateSnapshot): boolean {
    const requiredRoles = route.data['roles'] as AuthRole[] | undefined;
    if (!requiredRoles || requiredRoles.length === 0) {
      return true;
    }

    const userRoles = this.authService.getRoles();
    return requiredRoles.some(role => userRoles.includes(role));
  }
}
