import { Component, OnDestroy, OnInit } from '@angular/core';
import { Router } from '@angular/router';
import { Subject, takeUntil } from 'rxjs';
import { ManufacturingWorkspaceItem, MANUFACTURING_WORKSPACE_ITEMS } from '../../core/config/manufacturing-workspace.config';
import { PermissionHydrationState, PermissionService } from '../../core/services/permission.service';

@Component({
  selector: 'app-manufacturing-workspace-layout',
  templateUrl: './manufacturing-workspace-layout.component.html',
  styleUrls: ['./manufacturing-workspace-layout.component.scss']
})
export class ManufacturingWorkspaceLayoutComponent implements OnInit, OnDestroy {
  readonly items = MANUFACTURING_WORKSPACE_ITEMS;
  visibleItems: readonly ManufacturingWorkspaceItem[] = [];
  permissionHydrationState: PermissionHydrationState = 'idle';

  private readonly destroy$ = new Subject<void>();

  constructor(
    private readonly router: Router,
    private readonly permissionService: PermissionService
  ) {}

  ngOnInit(): void {
    this.permissionService.hydrationState$
      .pipe(takeUntil(this.destroy$))
      .subscribe((state) => {
        this.permissionHydrationState = state;
        this.visibleItems = state === 'ready' ? this.items.filter((item) => this.permissionService.hasAccess(item)) : [];
      });
  }

  ngOnDestroy(): void {
    this.destroy$.next();
    this.destroy$.complete();
  }

  isActive(item: ManufacturingWorkspaceItem): boolean {
    return this.router.url === item.route;
  }

  get isNavigationLoading(): boolean {
    return this.permissionHydrationState === 'idle' || this.permissionHydrationState === 'loading';
  }

  get activeRoute(): string {
    return this.router.url.split('?')[0];
  }
}
