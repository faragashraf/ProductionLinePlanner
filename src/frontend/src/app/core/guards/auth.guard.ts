import { Injectable } from '@angular/core';
import { CanActivate, Router, ActivatedRouteSnapshot, RouterStateSnapshot, UrlTree } from '@angular/router';
import { Observable, catchError, map, of } from 'rxjs';
import { AuthService } from '../services/auth.service';

@Injectable({
  providedIn: 'root'
})
export class AuthGuard implements CanActivate {
  constructor(
    private readonly authService: AuthService,
    private readonly router: Router
  ) {}

  canActivate(_route: ActivatedRouteSnapshot, state: RouterStateSnapshot): boolean | UrlTree | Observable<boolean | UrlTree> {
    const isAuthenticated = this.authService.isAuthenticated();
    if (!isAuthenticated) {
      return this.router.parseUrl('/login');
    }

    return this.authService.getCurrentUser().pipe(
      map(() => true),
      catchError(error => {
        if (error.status === 401) {
          this.authService.expireSession();
          return of(this.router.parseUrl('/login'));
        }

        return of(true);
      })
    );
  }
}
