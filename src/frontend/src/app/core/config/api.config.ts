import { environment } from '../../../environments/environment';

export const API_BASE_URL = environment.apiBaseUrl.replace(/\/$/, '');

export function buildApiUrl(path: string): string {
  const normalizedPath = path.startsWith('/') ? path : `/${path}`;
  return `${API_BASE_URL}${normalizedPath}`;
}
