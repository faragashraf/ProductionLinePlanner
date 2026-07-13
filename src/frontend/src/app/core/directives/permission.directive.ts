import { Directive, Input, OnDestroy, OnInit, TemplateRef, ViewContainerRef } from '@angular/core';
import { Subject, takeUntil } from 'rxjs';
import { PermissionRequirementDescriptor, PermissionService } from '../services/permission.service';

@Directive({
  selector: '[plpCan]'
})
export class PermissionDirective implements OnInit, OnDestroy {
  private readonly destroy$ = new Subject<void>();
  private requirement: PermissionRequirementDescriptor = {};
  private hasRendered = false;

  constructor(
    private readonly templateRef: TemplateRef<unknown>,
    private readonly viewContainer: ViewContainerRef,
    private readonly permissionService: PermissionService
  ) {}

  @Input()
  set plpCan(permissionOrDescriptor: string | string[] | PermissionRequirementDescriptor | null | undefined) {
    if (!permissionOrDescriptor) {
      this.requirement = {};
    } else if (typeof permissionOrDescriptor === 'string') {
      this.requirement = { permission: permissionOrDescriptor };
    } else if (Array.isArray(permissionOrDescriptor)) {
      this.requirement = { requireAny: permissionOrDescriptor };
    } else {
      this.requirement = permissionOrDescriptor;
    }

    this.applyVisibility();
  }

  @Input()
  set plpCanAny(value: string | string[] | undefined) {
    const normalized = this.normalizePermissionValues(value);
    if (normalized) {
      this.requirement.requireAny = normalized;
    }

    this.applyVisibility();
  }

  @Input()
  set plpCanAll(value: string | string[] | undefined) {
    const normalized = this.normalizePermissionValues(value);
    if (normalized) {
      this.requirement.requireAll = normalized;
    }

    this.applyVisibility();
  }

  ngOnInit(): void {
    this.permissionService.permissions$
      .pipe(takeUntil(this.destroy$))
      .subscribe(() => this.applyVisibility());

    this.permissionService.hydrationState$
      .pipe(takeUntil(this.destroy$))
      .subscribe(() => this.applyVisibility());

    this.applyVisibility();
  }

  ngOnDestroy(): void {
    this.destroy$.next();
    this.destroy$.complete();
  }

  /**
   * UX-only directive. Server-side authorization is still enforced through API/guards.
   */
  private applyVisibility(): void {
    if (this.permissionService.hydrationState !== 'ready') {
      this.viewContainer.clear();
      this.hasRendered = false;
      return;
    }

    if (this.permissionService.hasAccess(this.requirement)) {
      if (!this.hasRendered) {
        this.viewContainer.createEmbeddedView(this.templateRef);
        this.hasRendered = true;
      }
      return;
    }

    this.viewContainer.clear();
    this.hasRendered = false;
  }

  private normalizePermissionValues(value: string | string[] | undefined): string | string[] | undefined {
    if (!value) {
      return undefined;
    }

    if (typeof value === 'string') {
      const permission = value.trim();
      return permission.length > 0 ? permission : undefined;
    }

    const normalized = value
      .map((entry) => entry?.trim() ?? '')
      .filter((entry) => entry.length > 0);

    return normalized.length > 0 ? normalized : undefined;
  }
}
