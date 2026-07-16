import { PRODUCT_IDENTITY } from './product-identity.config';

describe('PRODUCT_IDENTITY', () => {
  it('keeps the current Arabic-only runtime identity in one immutable configuration', () => {
    expect(Object.isFrozen(PRODUCT_IDENTITY)).toBeTrue();
    expect(PRODUCT_IDENTITY.nameAr).toBe('منصة تخطيط خطوط الإنتاج');
    expect(PRODUCT_IDENTITY.workspaceNameAr).toBeTruthy();
    expect(PRODUCT_IDENTITY.logoLabelAr).toContain(PRODUCT_IDENTITY.nameAr);
  });
});
