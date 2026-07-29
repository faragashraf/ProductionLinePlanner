import { buildApiUrl, buildHubUrl, API_BASE_URL, HUB_BASE_URL } from './api.config';
import { environment as productionEnvironment } from '../../../environments/environment.production';

describe('application URL configuration', () => {
  it('uses a same-origin relative API base during development', () => {
    expect(API_BASE_URL).toBe('/api');
    expect(buildApiUrl('/api/auth/login')).toBe('/api/auth/login');
    expect(buildApiUrl('auth/login')).toBe('/api/auth/login');
  });

  it('does not generate a malformed duplicate API prefix', () => {
    expect(buildApiUrl('/api/workers')).toBe('/api/workers');
    expect(buildApiUrl('/workers')).toBe('/api/workers');
    expect(buildApiUrl('/api/api/workers')).not.toContain('/api/api/');
  });

  it('resolves hub URLs through the same-origin development proxy', () => {
    expect(HUB_BASE_URL).toBe('/hubs');
    expect(buildHubUrl('/hubs/production')).toBe('/hubs/production');
    expect(buildHubUrl('production')).toBe('/hubs/production');
  });

  it('uses the dedicated IIS API origin in production', () => {
    expect(productionEnvironment.production).toBeTrue();
    expect(productionEnvironment.apiBaseUrl).toBe('http://192.168.1.99:9000');
    expect(productionEnvironment.hubBaseUrl).toBe('http://192.168.1.99:9000');
  });
});
