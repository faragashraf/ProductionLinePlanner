import {
  PREVIEW_PIPELINE_BUILD_MARKER,
  initializeLocalDevelopmentRuntime,
  isLocalDevelopmentOrigin
} from './local-development-runtime';

describe('local development runtime', () => {
  const lanApiBaseUrl = 'http://tablet.local.test:5169';

  it('identifies the supported loopback and LAN development origins only', () => {
    expect(isLocalDevelopmentOrigin({ hostname: 'tablet.local.test', port: '4200' } as Location, lanApiBaseUrl)).toBeTrue();
    expect(isLocalDevelopmentOrigin({ hostname: 'localhost', port: '4200' } as Location, lanApiBaseUrl)).toBeTrue();
    expect(isLocalDevelopmentOrigin({ hostname: '127.0.0.1', port: '4200' } as Location, lanApiBaseUrl)).toBeTrue();
    expect(isLocalDevelopmentOrigin({ hostname: 'tablet.local.test', port: '4300' } as Location, lanApiBaseUrl)).toBeFalse();
    expect(isLocalDevelopmentOrigin({ hostname: 'production.example', port: '4200' } as Location, lanApiBaseUrl)).toBeFalse();
  });

  it('supports every host served by the same-origin development proxy', () => {
    expect(isLocalDevelopmentOrigin({ hostname: '192.168.1.6', port: '4200' } as Location, '/api')).toBeTrue();
    expect(isLocalDevelopmentOrigin({ hostname: 'localhost', port: '4200' } as Location, '/api')).toBeTrue();
    expect(isLocalDevelopmentOrigin({ hostname: '192.168.1.6', port: '4300' } as Location, '/api')).toBeFalse();
  });

  it('publishes the current build marker and unregisters only stale local registrations', async () => {
    const unregister = jasmine.createSpy('unregister').and.resolveTo(true);
    const target: Record<string, unknown> = {};
    const info = jasmine.createSpy('info');
    const serviceWorker = {
      getRegistrations: jasmine.createSpy('getRegistrations').and.resolveTo([{ unregister }])
    } as unknown as Pick<ServiceWorkerContainer, 'getRegistrations'>;

    initializeLocalDevelopmentRuntime(
      { hostname: 'tablet.local.test', port: '4200' } as Location,
      lanApiBaseUrl,
      target,
      serviceWorker,
      { info }
    );
    await Promise.resolve();
    await Promise.resolve();

    expect(target['__PLP_BUILD_MARKER__']).toBe(PREVIEW_PIPELINE_BUILD_MARKER);
    expect(unregister).toHaveBeenCalledTimes(1);
    expect(info).toHaveBeenCalledWith(`[PLP development build] ${PREVIEW_PIPELINE_BUILD_MARKER}`);
  });

  it('does not touch service-worker registrations outside the local development origins', () => {
    const getRegistrations = jasmine.createSpy('getRegistrations');

    initializeLocalDevelopmentRuntime(
      { hostname: 'production.example', port: '4200' } as Location,
      lanApiBaseUrl,
      {},
      { getRegistrations } as unknown as Pick<ServiceWorkerContainer, 'getRegistrations'>
    );

    expect(getRegistrations).not.toHaveBeenCalled();
  });
});
