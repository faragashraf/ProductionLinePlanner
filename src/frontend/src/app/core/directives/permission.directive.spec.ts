import { Component } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { BehaviorSubject } from 'rxjs';
import { PermissionDirective } from './permission.directive';
import { PermissionService } from '../services/permission.service';

class PermissionServiceStub {
  readonly permissions$ = new BehaviorSubject<string[]>([]);
  readonly hydrationState$ = new BehaviorSubject<'loading' | 'ready'>('loading');
  get hydrationState(): 'loading' | 'ready' { return this.hydrationState$.value; }
  hasAccess(requirement: any): boolean {
    const permissions = this.permissions$.value;
    if (requirement.permission) { return permissions.includes(requirement.permission); }
    if (requirement.requireAll) { return requirement.requireAll.every((item: string) => permissions.includes(item)); }
    return (requirement.requireAny || []).some((item: string) => permissions.includes(item));
  }
}

@Component({ template: '<span *plpCan="requirement">protected</span>' })
class HostComponent {
  requirement: any = 'users.view';
}

describe('PermissionDirective', () => {
  let fixture: ComponentFixture<HostComponent>;
  let permissions: PermissionServiceStub;

  beforeEach(() => {
    permissions = new PermissionServiceStub();
    TestBed.configureTestingModule({
      declarations: [HostComponent, PermissionDirective],
      providers: [{ provide: PermissionService, useValue: permissions }]
    });
    fixture = TestBed.createComponent(HostComponent);
  });

  it('hides during hydration and responds to single, any and all changes', () => {
    fixture.detectChanges();
    expect(fixture.nativeElement.textContent).not.toContain('protected');

    permissions.permissions$.next(['users.view', 'roles.view']);
    permissions.hydrationState$.next('ready');
    fixture.detectChanges();
    expect(fixture.nativeElement.textContent).toContain('protected');

    fixture.componentInstance.requirement = { requireAny: ['missing', 'roles.view'] };
    fixture.detectChanges();
    expect(fixture.nativeElement.textContent).toContain('protected');

    fixture.componentInstance.requirement = { requireAll: ['users.view', 'missing'] };
    fixture.detectChanges();
    expect(fixture.nativeElement.textContent).not.toContain('protected');
  });
});
