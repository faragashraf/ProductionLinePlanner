import { FormBuilder, FormControl, FormGroup, Validators } from '@angular/forms';
import { AdminUserCreateRequest, AdminUserDetails, AdminUserUpdateRequest } from '../../../core/services/iam-admin.service';

export type UserFormMode = 'create' | 'edit';

export interface UserFormControls {
  fullName: FormControl<string>;
  email: FormControl<string>;
  password: FormControl<string>;
  roleIds: FormControl<string[]>;
  isActive: FormControl<boolean>;
}

export type UserFormGroup = FormGroup<UserFormControls>;

export const USER_FORM_LIMITS = {
  fullName: 200,
  loginIdentifier: 200
} as const;

export function buildUserForm(formBuilder: FormBuilder, mode: UserFormMode, user?: AdminUserDetails): UserFormGroup {
  return formBuilder.nonNullable.group({
    fullName: [user?.fullName ?? '', [Validators.required, Validators.maxLength(USER_FORM_LIMITS.fullName)]],
    email: [user?.email ?? '', [Validators.required, Validators.maxLength(USER_FORM_LIMITS.loginIdentifier)]],
    password: ['', mode === 'create' ? [Validators.required] : []],
    roleIds: [user?.roleIds ?? [], [Validators.required, Validators.minLength(1)]],
    isActive: [user?.isActive ?? true]
  });
}

export function toCreateUserRequest(form: UserFormGroup): AdminUserCreateRequest {
  const value = form.getRawValue();
  return {
    fullName: value.fullName.trim(),
    email: value.email.trim().toLowerCase(),
    password: value.password,
    roleIds: [...value.roleIds],
    isActive: value.isActive
  };
}

export function toUpdateUserRequest(form: UserFormGroup): AdminUserUpdateRequest {
  const value = form.getRawValue();
  return {
    fullName: value.fullName.trim(),
    email: value.email.trim().toLowerCase(),
    roleIds: [...value.roleIds],
    isActive: value.isActive
  };
}
