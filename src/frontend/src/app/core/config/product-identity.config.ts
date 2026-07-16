/**
 * Static product identity for the current Arabic-only release.
 *
 * This is intentionally a simple configuration object, not a branding engine.
 * Runtime surfaces consume it so a future branding capability has one clear
 * replacement boundary without introducing tenant or theme behavior today.
 */
export const PRODUCT_IDENTITY = Object.freeze({
  nameAr: 'منصة تخطيط خطوط الإنتاج',
  nameEn: 'PRODUCTION LINE PLANNER',
  workspaceNameAr: 'مساحة العمل التشغيلية',
  platformLabelAr: 'منصة التصنيع',
  taglineAr: 'مساحة تشغيل موحّدة تُحوّل مراحل المصنع إلى خطة عمل واضحة وقابلة للمتابعة.',
  logoLabelAr: 'شعار منصة تخطيط خطوط الإنتاج',
  homeLabelAr: 'العودة إلى الرئيسية'
});

export type ProductIdentity = typeof PRODUCT_IDENTITY;
