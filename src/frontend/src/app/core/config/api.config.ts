import { environment } from '../../../environments/environment';

export const API_BASE_URL = normalizeBaseUrl(environment.apiBaseUrl);
export const HUB_BASE_URL = normalizeBaseUrl(environment.hubBaseUrl);

export function buildApiUrl(path: string): string {
  return buildApplicationUrl(API_BASE_URL, '/api', path);
}

export function buildHubUrl(path = ''): string {
  return buildApplicationUrl(HUB_BASE_URL, '/hubs', path);
}

function buildApplicationUrl(baseUrl: string, prefix: string, path: string): string {
  const normalizedPath = normalizePrefixedPath(prefix, path);

  if (!baseUrl || baseUrl === '/') return normalizedPath;
  if (baseUrl === prefix) return normalizedPath;

  return baseUrl.endsWith(prefix)
    ? `${baseUrl}${normalizedPath.slice(prefix.length)}`
    : `${baseUrl}${normalizedPath}`;
}

function normalizePrefixedPath(prefix: string, path: string): string {
  let normalizedPath = `/${path.trim().replace(/^\/+/, '')}`;
  const duplicatePrefix = `${prefix}${prefix}`;

  // Services can pass either `/workers` or `/api/workers`. Normalize a legacy
  // duplicate safely so a configuration change can never emit `/api/api/...`.
  while (normalizedPath === duplicatePrefix || normalizedPath.startsWith(`${duplicatePrefix}/`) || normalizedPath.startsWith(`${duplicatePrefix}?`)) {
    normalizedPath = normalizedPath.slice(prefix.length);
  }

  return normalizedPath === prefix || normalizedPath.startsWith(`${prefix}/`) || normalizedPath.startsWith(`${prefix}?`)
    ? normalizedPath
    : `${prefix}${normalizedPath}`;
}

function normalizeBaseUrl(value: string): string {
  const normalized = value.trim().replace(/\/+$/, '');
  return normalized || '/';
}
