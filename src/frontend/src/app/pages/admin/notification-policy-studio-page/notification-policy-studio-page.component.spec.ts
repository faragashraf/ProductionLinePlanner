import { of, throwError } from 'rxjs';
import { NotificationPolicyStudioPageComponent } from './notification-policy-studio-page.component';

describe('NotificationPolicyStudioPageComponent', () => {
  const policy: any = {
    eventKey: 'WorkerCreated', displayName: 'تم إنشاء عامل', allowedTokens: ['WorkerName'], isEnabled: false,
    severity: 'Information', isToastEnabled: true, isInboxEnabled: true, isSoundEnabled: false, isBrowserEnabled: false,
    soundKey: null, titleTemplateAr: 'عامل {WorkerName}', messageTemplateAr: 'تم إنشاء {WorkerName}', rowVersion: 'AQI=', recipientRules: [], updatedAtUtc: '2026-07-20T00:00:00Z'
  };

  function create(overrides: any = {}): { component: NotificationPolicyStudioPageComponent; api: any } {
    const api = {
      listPolicies: jasmine.createSpy().and.returnValue(of([policy])),
      getRecipientOptions: jasmine.createSpy().and.returnValue(of({ users: [], roles: [], permissions: [], capabilityGroups: [] })),
      getPolicy: jasmine.createSpy().and.returnValue(of(policy)),
      updatePolicy: jasmine.createSpy().and.returnValue(of({ ...policy, isEnabled: true })),
      ...overrides
    };
    return { component: new NotificationPolicyStudioPageComponent(api), api };
  }

  it('loads and renders the first static event policy', () => {
    const { component } = create();
    component.ngOnInit();
    expect(component.policies.length).toBe(1);
    expect(component.draft?.eventKey).toBe('WorkerCreated');
  });

  it('filters events by search, state, and severity', () => {
    const { component } = create();
    component.policies = [{ ...policy, isEnabled: true, severity: 'Warning' }];
    component.searchTerm = 'Worker';
    component.enabledFilter = 'enabled';
    component.severityFilter = 'Warning';
    expect(component.filteredPolicies.length).toBe(1);
  });

  it('inserts tokens and previews their sample values', () => {
    const { component } = create();
    component.draft = { ...policy, recipientRules: [] };
    component.insertToken('WorkerName', 'message');
    expect(component.draft!.messageTemplateAr).toContain('{WorkerName}');
    expect(component.preview('{WorkerName}')).toBe('أحمد محمد');
  });

  it('saves recipient rules with stable sort order', () => {
    const { component, api } = create();
    component.draft = { ...policy, recipientRules: [{ recipientKind: 'Creator', isExcludeActor: false, sortOrder: 4, isActive: true }] };
    component.save();
    expect(api.updatePolicy).toHaveBeenCalled();
    expect(api.updatePolicy.calls.mostRecent().args[1].recipientRules[0].sortOrder).toBe(0);
  });

  it('surfaces API errors during save', () => {
    const { component } = create({ updatePolicy: () => throwError(() => new Error('Concurrency conflict')) });
    component.draft = { ...policy, recipientRules: [] };
    component.save();
    expect(component.hasError).toBeTrue();
    expect(component.errorMessage).toBe('Concurrency conflict');
  });
});
