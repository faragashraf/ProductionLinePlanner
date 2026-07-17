import { PRODUCT_IDENTITY } from './product-identity.config';

describe('PRODUCT_IDENTITY', () => {
  it('keeps the current runtime identity in one immutable configuration', () => {
    expect(Object.isFrozen(PRODUCT_IDENTITY)).toBeTrue();
    expect({
      nameAr: PRODUCT_IDENTITY.nameAr,
      nameEn: PRODUCT_IDENTITY.nameEn,
      workspaceNameAr: PRODUCT_IDENTITY.workspaceNameAr,
      platformLabelAr: PRODUCT_IDENTITY.platformLabelAr
    }).toEqual({
      nameAr: 'منصة ديوب',
      nameEn: 'DAYOUB',
      workspaceNameAr: 'منظومة الإدارة والتشغيل المتكاملة',
      platformLabelAr: 'منصة الأعمال المؤسسية'
    });
    expect(PRODUCT_IDENTITY.logoLabelAr).toContain(PRODUCT_IDENTITY.nameAr);
  });
});
