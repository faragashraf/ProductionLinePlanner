import { CommonModule } from '@angular/common';
import { NO_ERRORS_SCHEMA } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { Router } from '@angular/router';
import { BehaviorSubject } from 'rxjs';
import { PermissionRequirementDescriptor } from '../../core/authorization/permission-requirement';
import { PERMISSIONS } from '../../core/config/permission-identifiers';
import { PermissionService } from '../../core/services/permission.service';
import { ManufacturingWorkspaceLayoutComponent } from './manufacturing-workspace-layout.component';

describe('ManufacturingWorkspaceLayoutComponent', () => {
  function configure(permissions: string[], url = '/manufacturing/dashboard'): ComponentFixture<ManufacturingWorkspaceLayoutComponent> {
    const hydrationState$ = new BehaviorSubject<'ready'>('ready');
    const permissionService = {
      hydrationState$: hydrationState$.asObservable(),
      hasAccess: (requirement: PermissionRequirementDescriptor) => {
        if (requirement.permission) return permissions.includes(requirement.permission);
        if (requirement.requireAny) return (Array.isArray(requirement.requireAny) ? requirement.requireAny : [requirement.requireAny]).some(permission => permissions.includes(permission));
        if (requirement.requireAll) return (Array.isArray(requirement.requireAll) ? requirement.requireAll : [requirement.requireAll]).every(permission => permissions.includes(permission));
        return true;
      }
    };

    TestBed.configureTestingModule({
      declarations: [ManufacturingWorkspaceLayoutComponent],
      imports: [CommonModule],
      providers: [
        { provide: Router, useValue: { url } },
        { provide: PermissionService, useValue: permissionService }
      ],
      schemas: [NO_ERRORS_SCHEMA]
    });

    const fixture = TestBed.createComponent(ManufacturingWorkspaceLayoutComponent);
    fixture.detectChanges();
    return fixture;
  }

  afterEach(() => TestBed.resetTestingModule());

  it('renders the Production Recording workspace tab for a user who can view and record production', () => {
    const fixture = configure([PERMISSIONS.production.view, PERMISSIONS.production.record]);

    const tab = fixture.nativeElement.querySelector('[data-workspace-route="/manufacturing/production-recording"]') as HTMLAnchorElement | null;
    expect(tab?.textContent).toContain('تسجيل الإنتاج');
  });

  it('hides the Production Recording workspace tab when recording permission is absent', () => {
    const fixture = configure([PERMISSIONS.production.view]);

    expect(fixture.nativeElement.querySelector('[data-workspace-route="/manufacturing/production-recording"]')).toBeNull();
  });

  it('keeps the Production Recording tab active when stage context is present in the route query', () => {
    const fixture = configure(
      [PERMISSIONS.production.view, PERMISSIONS.production.record],
      '/manufacturing/production-recording?factoryId=factory-1&productionLineId=line-1&mainStageId=main-1&subStageId=sub-1'
    );

    const tab = fixture.nativeElement.querySelector('[data-workspace-route="/manufacturing/production-recording"]') as HTMLAnchorElement | null;
    expect(tab?.classList.contains('manufacturing-workspace__nav-item--active')).toBeTrue();
    expect(tab?.getAttribute('aria-current')).toBe('page');
  });

  it('keeps the full workspace hero on the dashboard', () => {
    const fixture = configure([PERMISSIONS.production.view], '/manufacturing/dashboard');
    expect(fixture.nativeElement.querySelector('.manufacturing-workspace__header--compact')).toBeNull();
    expect(fixture.nativeElement.querySelector('.manufacturing-workspace__header h1')?.textContent).toContain('مساحة التصنيع');
  });

  it('uses a compact context header on child routes without duplicating the page h1', () => {
    const fixture = configure([PERMISSIONS.production.view], '/manufacturing/daily-production-operations');
    expect(fixture.nativeElement.querySelector('.manufacturing-workspace__header--compact')).not.toBeNull();
    expect(fixture.nativeElement.querySelector('.manufacturing-workspace__header h1')).toBeNull();
    expect(fixture.nativeElement.querySelector('.manufacturing-workspace__header h2')?.textContent).toContain('مساحة التصنيع');
  });
});
