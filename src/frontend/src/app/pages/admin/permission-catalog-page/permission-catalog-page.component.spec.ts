import { of, throwError } from 'rxjs';
import { PermissionCatalogPageComponent } from './permission-catalog-page.component';

describe('PermissionCatalogPageComponent', () => {
  function createComponent(getPermissionCatalog: jasmine.Spy): PermissionCatalogPageComponent {
    return new PermissionCatalogPageComponent({ getPermissionCatalog } as any);
  }

  it('shows an empty catalog only after a successful empty response', () => {
    const component = createComponent(jasmine.createSpy('getPermissionCatalog').and.returnValue(of([])));

    component.loadCatalog(true);

    expect(component.hasError).toBeFalse();
    expect(component.catalog).toEqual([]);
    expect(component.filtered).toEqual([]);
  });

  it('preserves the error state after a failed catalog request', () => {
    const component = createComponent(jasmine.createSpy('getPermissionCatalog').and.returnValue(throwError(() => new Error('Catalog unavailable'))));

    component.loadCatalog(true);

    expect(component.hasError).toBeTrue();
    expect(component.errorMessage).toBe('Catalog unavailable');
    expect(component.isLoading).toBeFalse();
  });

  it('clears a catalog error after a successful retry', () => {
    const getPermissionCatalog = jasmine.createSpy('getPermissionCatalog').and.returnValues(
      throwError(() => new Error('Catalog unavailable')),
      of([])
    );
    const component = createComponent(getPermissionCatalog);

    component.loadCatalog(true);
    component.loadCatalog(true);

    expect(component.hasError).toBeFalse();
    expect(component.errorMessage).toBeNull();
    expect(component.catalog).toEqual([]);
  });
});
