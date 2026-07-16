import { FormBuilder } from '@angular/forms';
import { buildUserForm, toCreateUserRequest, toUpdateUserRequest } from './user-form.model';

describe('user form capability', () => {
  const formBuilder = new FormBuilder();

  it('uses no email-format validator and accepts text usernames', () => {
    const form = buildUserForm(formBuilder, 'create');
    form.controls.email.setValue('factory.manager');
    expect(form.controls.email.valid).toBeTrue();
    expect(form.controls.email.hasError('email')).toBeFalse();
  });

  it('requires password only for create', () => {
    expect(buildUserForm(formBuilder, 'create').controls.password.hasError('required')).toBeTrue();
    expect(buildUserForm(formBuilder, 'edit').controls.password.hasError('required')).toBeFalse();
  });

  it('keeps password out of update mapping', () => {
    const form = buildUserForm(formBuilder, 'edit');
    form.setValue({ fullName: 'Name', email: 'username', password: 'ignored', roleIds: ['role'], isActive: true });
    expect((toUpdateUserRequest(form) as any).password).toBeUndefined();
    expect(toCreateUserRequest(buildUserForm(formBuilder, 'create')).email).toBe('');
  });
});
