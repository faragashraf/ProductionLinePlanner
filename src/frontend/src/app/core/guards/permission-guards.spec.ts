import { PermissionCanActivateGuard } from './permission-can-activate.guard';
import { PermissionCanMatchGuard } from './permission-can-match.guard';

describe('permission guards', () => {
  it('delegates canActivate and canMatch to the shared evaluator', () => {
    const evaluator = { evaluate: jasmine.createSpy('evaluate').and.returnValue(true) };
    const activate = new PermissionCanActivateGuard(evaluator as any);
    const match = new PermissionCanMatchGuard(evaluator as any);

    expect(activate.canActivate({ data: { permission: 'users.view' } } as any, {} as any)).toBeTrue();
    expect(match.canMatch({ data: { requireAny: ['users.view'] } } as any, [])).toBeTrue();
    expect(evaluator.evaluate).toHaveBeenCalledTimes(2);
  });
});
