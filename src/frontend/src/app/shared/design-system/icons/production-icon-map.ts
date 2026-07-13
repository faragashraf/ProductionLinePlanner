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
  | 'menu';

export type ProductionIconClass = `pi-${string}`;

export const PRODUCTION_ICON_MAP = {
  add: 'pi-plus',
  edit: 'pi-pencil',
  save: 'pi-save',
  cancel: 'pi-times',
  delete: 'pi-trash',
  activate: 'pi-check-circle',
  deactivate: 'pi-ban',
  search: 'pi-search',
  filter: 'pi-filter',
  refresh: 'pi-refresh',
  export: 'pi-download',
  import: 'pi-upload',
  view: 'pi-eye',
  approve: 'pi-check',
  reject: 'pi-times-circle',
  close: 'pi-times',
  menu: 'pi-bars'
} as const satisfies Readonly<Record<ProductionIconAction, ProductionIconClass>>;

export type ProductionIconName = (typeof PRODUCTION_ICON_MAP)[ProductionIconAction];
export type ProductionLayoutDirection = 'rtl' | 'ltr';
export type ProductionNavigationAction = 'back' | 'forward';

export function productionIconFor(action: ProductionIconAction): ProductionIconName {
  return PRODUCTION_ICON_MAP[action];
}

/** Returns a semantic navigation icon that points in the reading direction. */
export function productionNavigationIconFor(
  action: ProductionNavigationAction,
  direction: ProductionLayoutDirection = 'rtl'
): ProductionIconClass {
  const pointsRight = (action === 'back' && direction === 'rtl') || (action === 'forward' && direction === 'ltr');
  return pointsRight ? 'pi-arrow-right' : 'pi-arrow-left';
}
