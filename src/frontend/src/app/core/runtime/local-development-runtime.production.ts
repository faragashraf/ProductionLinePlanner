export const PREVIEW_PIPELINE_BUILD_MARKER = '';

/** Production intentionally excludes local preview and service-worker cleanup. */
export function initializeLocalDevelopmentRuntime(
  _location: Pick<Location, 'hostname' | 'port'>,
  _apiBaseUrl: string,
  _target: Record<string, unknown>,
  _serviceWorkerContainer: Pick<ServiceWorkerContainer, 'getRegistrations'> | undefined,
  _log: Pick<Console, 'info'> = console
): void {}
