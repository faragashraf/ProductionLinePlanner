import { Injectable } from '@angular/core';

export type PlaceholderRole = 'Viewer' | 'Admin' | 'SuperAdmin';

@Injectable({
  providedIn: 'root'
})
export class AuthService {
  private readonly _isAuthenticated = true;
  private readonly _roles: PlaceholderRole[] = ['Admin', 'SuperAdmin'];
  private readonly _userName = 'placeholder.user';

  isAuthenticated(): boolean {
    return this._isAuthenticated;
  }

  hasRole(role: PlaceholderRole): boolean {
    return this._roles.includes(role);
  }

  getRoles(): PlaceholderRole[] {
    return [...this._roles];
  }

  get userName(): string {
    return this._userName;
  }

  // Placeholder hooks - no real auth call yet.
  login(_email: string, _password: string): void {
    return;
  }

  logout(): void {
    return;
  }
}
