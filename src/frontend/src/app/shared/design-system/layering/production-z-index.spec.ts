import { PRODUCTION_RUNTIME_Z_INDEX } from './production-z-index';

describe('Production runtime z-index hierarchy', () => {
  it('keeps overlays above content and toast below tooltip', () => {
    expect(PRODUCTION_RUNTIME_Z_INDEX.sticky).toBeLessThan(PRODUCTION_RUNTIME_Z_INDEX.dropdown);
    expect(PRODUCTION_RUNTIME_Z_INDEX.dropdown).toBeLessThan(PRODUCTION_RUNTIME_Z_INDEX.menu);
    expect(PRODUCTION_RUNTIME_Z_INDEX.menu).toBeLessThan(PRODUCTION_RUNTIME_Z_INDEX.modal);
    expect(PRODUCTION_RUNTIME_Z_INDEX.modal).toBeLessThan(PRODUCTION_RUNTIME_Z_INDEX.toast);
    expect(PRODUCTION_RUNTIME_Z_INDEX.toast).toBeLessThan(PRODUCTION_RUNTIME_Z_INDEX.tooltip);
  });
});
