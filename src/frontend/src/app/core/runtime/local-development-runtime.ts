const LOOPBACK_DEVELOPMENT_HOSTS = new Set(['localhost', '127.0.0.1']);

// This is intentionally a short capability marker, not a source revision or
// repository identifier. It makes an old browser bundle visible in DevTools.
export const PREVIEW_PIPELINE_BUILD_MARKER = 'plp-preview-post-contract-20260716';

export function isLocalDevelopmentOrigin(
  location: Pick<Location, 'hostname' | 'port'>,
  apiBaseUrl: string
): boolean {
  if (location.port !== '4200') return false;

  // A relative API base means the Angular dev server is the explicit local
  // runtime boundary. Any host at its development port is supported, including
  // the Mac's changing LAN address used by tablets.
  if (apiBaseUrl.startsWith('/')) return true;

  return LOOPBACK_DEVELOPMENT_HOSTS.has(location.hostname) || location.hostname === apiHostname(apiBaseUrl);
}

export function initializeLocalDevelopmentRuntime(
  location: Pick<Location, 'hostname' | 'port'>,
  apiBaseUrl: string,
  target: Record<string, unknown>,
  serviceWorkerContainer: Pick<ServiceWorkerContainer, 'getRegistrations'> | undefined,
  log: Pick<Console, 'info'> = console
): void {
  if (!isLocalDevelopmentOrigin(location, apiBaseUrl)) return;

  target['__PLP_BUILD_MARKER__'] = PREVIEW_PIPELINE_BUILD_MARKER;
  log.info(`[PLP development build] ${PREVIEW_PIPELINE_BUILD_MARKER}`);

  // This application has no PWA/service-worker configuration. Removing a
  // controller registered by an older local build prevents it from serving a
  // stale shell or replaying requests on the local development origin only.
  if (!serviceWorkerContainer) return;

  void serviceWorkerContainer.getRegistrations()
    .then(registrations => Promise.all(registrations.map(registration => registration.unregister())))
    .then(results => {
      const removed = results.filter(Boolean).length;
      if (removed > 0) log.info(`[PLP development build] removed ${removed} stale local service-worker registration(s); reload once.`);
    })
    .catch(() => {
      // Development diagnostics must never block application bootstrap.
    });
}

function apiHostname(apiBaseUrl: string): string | null {
  try {
    return new URL(apiBaseUrl).hostname;
  } catch {
    return null;
  }
}
