/**
 * The product icon language. New UI uses PrimeIcons through this map instead
 * of introducing per-page icon literals or a second icon library.
 */
export type ProductionIconAction =
  | 'add'
  | 'edit'
  | 'save'
  | 'cancel'
  | 'delete'
  | 'activate'
  | 'deactivate'
  | 'search'
  | 'filter'
  | 'refresh'
  | 'export'
  | 'import'
  | 'view'
  | 'approve'
  | 'reject'
  | 'close'
  | 'menu'
  | 'signOut';

export type ProductionIconClass = `pi pi-${string}`;

export const PRODUCTION_ICON_MAP = {
  add: 'pi pi-plus',
  edit: 'pi pi-pencil',
  save: 'pi pi-save',
  cancel: 'pi pi-times',
  delete: 'pi pi-trash',
  activate: 'pi pi-check-circle',
  deactivate: 'pi pi-ban',
  search: 'pi pi-search',
  filter: 'pi pi-filter',
  refresh: 'pi pi-refresh',
  export: 'pi pi-download',
  import: 'pi pi-upload',
  view: 'pi pi-eye',
  approve: 'pi pi-check',
  reject: 'pi pi-times-circle',
  close: 'pi pi-times',
  menu: 'pi pi-bars',
  signOut: 'pi pi-sign-out'
} as const satisfies Readonly<Record<ProductionIconAction, ProductionIconClass>>;

export type ProductionIconName = (typeof PRODUCTION_ICON_MAP)[ProductionIconAction];
export type ProductionLayoutDirection = 'rtl' | 'ltr';
export type ProductionNavigationAction = 'back' | 'forward';

export function productionIconFor(action: ProductionIconAction): ProductionIconName {
  return PRODUCTION_ICON_MAP[action];
}

/** Normalizes existing glyph-only values at compatibility boundaries. */
export function normalizePrimeIconClass(icon: string): ProductionIconClass {
  const glyph = icon.trim().split(/\s+/).find((value) => value.startsWith('pi-')) ?? 'pi-info-circle';
  return `pi ${glyph}` as ProductionIconClass;
}

/** Returns a semantic navigation icon that points in the reading direction. */
export function productionNavigationIconFor(
  action: ProductionNavigationAction,
  direction: ProductionLayoutDirection = 'rtl'
): ProductionIconClass {
  const pointsRight = (action === 'back' && direction === 'rtl') || (action === 'forward' && direction === 'ltr');
  return pointsRight ? 'pi pi-arrow-right' : 'pi pi-arrow-left';
}
