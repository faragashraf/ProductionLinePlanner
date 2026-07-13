import {
  PRODUCTION_STATUS_MAP,
  PRODUCTION_VISUAL_TONE_MAP,
  ProductionStatusKey,
  resolveProductionStatus
} from './production-status-map';

describe('Production status language', () => {
  const requiredStatuses: ProductionStatusKey[] = [
    'draft',
    'approved',
    'cancelled',
    'active',
    'inactive',
    'pending',
    'success',
    'warning',
    'danger',
    'info',
    'neutral'
  ];

  it('provides one shared tone, PrimeNG severity, and PrimeIcon for every standard status', () => {
    requiredStatuses.forEach((status) => {
      const meta = PRODUCTION_STATUS_MAP[status];
      expect(meta.icon).toMatch(/^pi-/);
      expect(PRODUCTION_VISUAL_TONE_MAP[meta.tone].token).toMatch(/^--plp-color-/);
      expect(PRODUCTION_VISUAL_TONE_MAP[meta.tone].primeSeverity).toBeTruthy();
    });
  });

  it('normalizes the supported cancelled spelling without exposing a raw color', () => {
    expect(resolveProductionStatus('cancelled').key).toBe('cancelled');
    expect(resolveProductionStatus('canceled').key).toBe('cancelled');
    expect(resolveProductionStatus('unknown').key).toBe('neutral');
  });
});
