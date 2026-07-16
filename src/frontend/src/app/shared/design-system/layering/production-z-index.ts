import { PrimeNGConfig } from 'primeng/api';

/** Runtime companion to the CSS --plp-z-* hierarchy. */
export const PRODUCTION_RUNTIME_Z_INDEX = {
  base: 0,
  sticky: 100,
  dropdown: 1000,
  menu: 1050,
  modal: 1200,
  toast: 1300,
  tooltip: 1400
} as const;

const PLP_ARIA_TRANSLATIONS = {
  firstPageLabel: 'الصفحة الأولى',
  lastPageLabel: 'الصفحة الأخيرة',
  nextPageLabel: 'الصفحة التالية',
  prevPageLabel: 'الصفحة السابقة',
  previousPageLabel: 'الصفحة السابقة',
  rowsPerPageLabel: 'عدد الصفوف في الصفحة',
  pageLabel: 'الصفحة {page}',
  listLabel: 'قائمة الخيارات'
} as const;

export function configureProductionPrimeNg(config: PrimeNGConfig): void {
  config.ripple = true;
  config.setTranslation({
    emptyMessage: 'لا توجد بيانات متاحة',
    aria: {
      ...config.translation.aria,
      ...PLP_ARIA_TRANSLATIONS
    }
  });
  config.zIndex = {
    modal: PRODUCTION_RUNTIME_Z_INDEX.modal,
    overlay: PRODUCTION_RUNTIME_Z_INDEX.dropdown,
    menu: PRODUCTION_RUNTIME_Z_INDEX.menu,
    tooltip: PRODUCTION_RUNTIME_Z_INDEX.tooltip
  };
}
