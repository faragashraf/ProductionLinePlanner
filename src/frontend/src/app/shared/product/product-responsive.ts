/**
 * Product-layer responsive contracts. Components use the same values as the
 * global Design System; page code should not create local breakpoint rules.
 */
export type PlpDialogSize = 'compact' | 'standard' | 'wide';

export const PLP_RESPONSIVE_CONTRACT = {
  phoneMax: 599,
  tabletPortraitMin: 600,
  tabletLandscapeMin: 768,
  desktopMin: 1024,
  wideDesktopMin: 1280,
  dialogGutter: {
    phone: 'var(--plp-space-16)',
    tabletPortrait: 'var(--plp-space-20)',
    tabletLandscape: 'var(--plp-space-24)'
  }
} as const;

export const PLP_DIALOG_SIZE_CLASS: Readonly<Record<PlpDialogSize, string>> = {
  compact: 'plp-product-dialog--compact',
  standard: 'plp-product-dialog--standard',
  wide: 'plp-product-dialog--wide'
};
