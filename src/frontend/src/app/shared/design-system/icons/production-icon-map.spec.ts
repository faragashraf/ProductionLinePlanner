import {
  PRODUCTION_ICON_MAP,
  ProductionIconAction,
  productionIconFor,
  productionNavigationIconFor
} from './production-icon-map';

describe('Production icon language', () => {
  const requiredActions: ProductionIconAction[] = [
    'add',
    'edit',
    'save',
    'cancel',
    'delete',
    'activate',
    'deactivate',
    'search',
    'filter',
    'refresh',
    'export',
    'import',
    'view',
    'approve',
    'reject',
    'close',
    'menu'
  ];

  it('maps every standard action to a PrimeIcon', () => {
    requiredActions.forEach((action) => {
      expect(productionIconFor(action)).toMatch(/^pi-/);
    });
  });

  it('keeps the map complete and immutable by action key', () => {
    expect(Object.keys(PRODUCTION_ICON_MAP).sort()).toEqual([...requiredActions].sort());
  });

  it('uses reading-direction-aware navigation arrows', () => {
    expect(productionNavigationIconFor('back', 'rtl')).toBe('pi-arrow-right');
    expect(productionNavigationIconFor('forward', 'rtl')).toBe('pi-arrow-left');
    expect(productionNavigationIconFor('back', 'ltr')).toBe('pi-arrow-left');
  });
});
