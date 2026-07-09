export type AuthRole = 'Viewer' | 'Admin' | 'SuperAdmin' | string;

export interface LoginRequest {
  email: string;
  password: string;
}

export interface AuthLoginResponse {
  accessToken: string;
  refreshToken?: string | null;
  expiresAt: string;
  userId: string;
  roles: AuthRole[];
  permissions: string[];
}

export interface CurrentUserResponse {
  id: string;
  fullName: string;
  email: string;
  roles: AuthRole[];
  permissions: string[];
}

export interface AuthUser {
  id: string;
  fullName: string;
  email: string;
  roles: AuthRole[];
  permissions: string[];
}
