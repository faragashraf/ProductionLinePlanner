import { of, throwError } from 'rxjs';
import { AdminRolesPageComponent } from './admin-roles-page.component';

describe('AdminRolesPageComponent', () => {
  const role = { id: 'role-1', role: 'Shift Lead', name: 'Shift Lead', description: 'Initial', isSystemRole: false, isActive: true, assignedUsers: 0, permissions: [] };

  function createComponent(overrides: any = {}): { component: AdminRolesPageComponent; admin: any } {
    const admin = {
      createRole: jasmine.createSpy('createRole').and.returnValue(of(role)),
      updateRole: jasmine.createSpy('updateRole').and.returnValue(of({ ...role, description: null })),
      ...overrides
    };
    const component = new AdminRolesPageComponent(admin, { has: () => false } as any, { confirm: () => true } as any);
    return { component, admin };
  }

  it('shows the backend validation message from a create request', () => {
    const { component } = createComponent({ createRole: () => throwError(() => new Error('Role name already exists.')) });
    component.draft = { name: 'Shift Lead', description: '', isActive: true };

    component.saveRole();

    expect(component.hasError).toBeTrue();
    expect(component.errorMessage).toBe('Role name already exists.');
  });

  it('keeps the empty description returned after an explicit clear', () => {
    const { component, admin } = createComponent();
    component.editRole(role);
    component.draft.description = '';

    component.saveRole();

    expect(admin.updateRole).toHaveBeenCalledWith('role-1', { name: 'Shift Lead', description: null, isActive: true });
    expect(component.draft.description).toBe('');
  });

  it('does not invoke a forbidden system-role delete action', () => {
    const { component, admin } = createComponent({ deleteRole: jasmine.createSpy('deleteRole') });
    component.deleteRole({ ...role, isSystemRole: true });

    expect(admin.deleteRole).not.toHaveBeenCalled();
  });

  it('does not save a system-role definition and exposes the shared description limit', () => {
    const { component, admin } = createComponent();
    component.editRole({ ...role, isSystemRole: true });

    component.saveRole();

    expect(admin.updateRole).not.toHaveBeenCalled();
    expect(component.maxDescriptionLength).toBe(500);
  });
});
