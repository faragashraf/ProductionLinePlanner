import { Component, ElementRef, HostListener, OnDestroy, OnInit, ViewChild } from '@angular/core';
import { ActivatedRoute, NavigationEnd, Router } from '@angular/router';
import { MenuItem } from 'primeng/api';
import { Subject, filter, takeUntil, map } from 'rxjs';
import { APP_NAVIGATION_ITEMS, AppNavigationItem } from '../../core/config/navigation.config';
import { PermissionHydrationState, PermissionService } from '../../core/services/permission.service';
import { AuthService } from '../../core/services/auth.service';
import { PRODUCTION_RUNTIME_Z_INDEX } from '../../shared/design-system/layering/production-z-index';
import { PLP_ANGULAR_MOTION } from '../../shared/product/product-motion';
import { PRODUCT_IDENTITY } from '../../core/config/product-identity.config';
import { productionIconFor } from '../../shared/design-system/icons/production-icon-map';

export type ShellNavigationMode = 'phone' | 'tablet-portrait' | 'tablet-landscape' | 'desktop';

@Component({
  selector: 'app-shell',
  templateUrl: './app-shell.component.html',
  styleUrls: ['./app-shell.component.scss']
})
export class AppShellComponent implements OnInit, OnDestroy {
  @ViewChild('menuTrigger') private menuTrigger?: ElementRef<HTMLButtonElement>;
  @ViewChild('overlayCloseButton') private overlayCloseButton?: ElementRef<HTMLButtonElement>;

  sidebarOpen = false;
  sidebarCollapsed = false;
  navigationMode: ShellNavigationMode = 'phone';
  breadcrumbItems: MenuItem[] = [];
  readonly overlaySidebarBaseZIndex = PRODUCTION_RUNTIME_Z_INDEX.modal;
  readonly sidebarTransitionOptions = PLP_ANGULAR_MOTION.sidebar;
  readonly productIdentity = PRODUCT_IDENTITY;
  readonly menuIcon = productionIconFor('menu');
  readonly closeIcon = productionIconFor('close');
  readonly signOutIcon = productionIconFor('signOut');

  navigationItems: AppNavigationItem[] = [];
  workspaceNavigationItems: AppNavigationItem[] = [];
  administrationNavigationItems: AppNavigationItem[] = [];
  permissionHydrationState: PermissionHydrationState = 'idle';
  currentPageLabel = 'لوحة التحكم';

  private destroy$ = new Subject<void>();

  constructor(
    private readonly router: Router,
    private readonly activatedRoute: ActivatedRoute,
    private readonly authService: AuthService,
    private readonly permissionService: PermissionService
  ) {}

  ngOnInit(): void {
    this.checkViewport();

    this.permissionService.hydrationState$
      .pipe(takeUntil(this.destroy$))
      .subscribe((state) => {
        this.permissionHydrationState = state;
      });

    this.permissionService.permissions$
      .pipe(
        map(() => this.permissionService.filterNavigation(APP_NAVIGATION_ITEMS)),
        takeUntil(this.destroy$)
      )
      .subscribe((items) => {
        this.navigationItems = items;
        this.workspaceNavigationItems = items.filter((item) => item.group === 'workspace');
        this.administrationNavigationItems = items.filter((item) => item.group === 'administration');
      });

    this.hydrateNavigation();

    this.router.events
      .pipe(
        filter((event): event is NavigationEnd => event instanceof NavigationEnd),
        takeUntil(this.destroy$)
      )
      .subscribe(() => {
        this.updateBreadcrumbs();
        if (this.isOverlayNavigation) {
          this.closeSidebar();
        }
      });

    this.updateBreadcrumbs();
  }

  ngOnDestroy(): void {
    this.destroy$.next();
    this.destroy$.complete();
  }

  @HostListener('window:resize')
  onResize(): void {
    this.checkViewport();

    if (this.isOverlayNavigation) {
      this.sidebarOpen = false;
      this.sidebarCollapsed = false;
      return;
    }

    this.sidebarOpen = false;
  }

  @HostListener('document:keydown.escape', ['$event'])
  onEscape(event: KeyboardEvent): void {
    if (this.isOverlayNavigation && this.sidebarOpen) {
      event.preventDefault();
      this.closeSidebar();
    }
  }

  toggleSidebar(): void {
    if (this.isOverlayNavigation) {
      this.sidebarOpen = !this.sidebarOpen;
      return;
    }

    this.sidebarCollapsed = !this.sidebarCollapsed;
  }

  closeSidebar(): void {
    if (this.isOverlayNavigation) {
      this.sidebarOpen = false;
      return;
    }

    this.sidebarCollapsed = true;
  }

  onSidebarVisibleChange(visible: boolean): void {
    if (!visible) {
      this.closeSidebar();
    }
  }

  onOverlayNavigationShow(): void {
    this.overlayCloseButton?.nativeElement.focus();
  }

  onOverlayNavigationHide(): void {
    this.sidebarOpen = false;
    this.menuTrigger?.nativeElement.focus();
  }

  onNavigationSelected(closeAfterNavigation: boolean): void {
    if (closeAfterNavigation) {
      this.closeSidebar();
    }
  }

  isActive(path: string): boolean {
    const currentPath = this.router.url.split(/[?#]/, 1)[0];
    if (currentPath === path || currentPath.startsWith(`${path}/`)) {
      return true;
    }

    const dashboardSuffix = '/dashboard';
    const workspacePath = path.endsWith(dashboardSuffix)
      ? path.slice(0, -dashboardSuffix.length)
      : '';

    return Boolean(workspacePath)
      && (currentPath === workspacePath || currentPath.startsWith(`${workspacePath}/`));
  }

  logout(): void {
    this.authService.logout();
    this.router.navigateByUrl('/login');
  }

  get isNavigationLoading(): boolean {
    return this.permissionHydrationState === 'loading';
  }

  get showNavigation(): boolean {
    return this.permissionHydrationState !== 'loading';
  }

  get isOverlayNavigation(): boolean {
    return this.navigationMode === 'phone' || this.navigationMode === 'tablet-portrait';
  }

  get hasPersistentNavigation(): boolean {
    return !this.isOverlayNavigation;
  }

  get currentUserName(): string {
    return this.authService.userName || 'الحساب';
  }

  get currentUserInitial(): string {
    return this.currentUserName.trim().charAt(0) || 'ح';
  }

  retryNavigationHydration(): void {
    this.hydrateNavigation();
  }

  trackByNavigationId(_index: number, item: AppNavigationItem): string {
    return item.id;
  }

  private buildBreadcrumbs(routeSnapshot: any): MenuItem[] {
    const items: MenuItem[] = [
      { label: 'الرئيسية', routerLink: '/dashboard' }
    ];
    const stack = this.collectBreadcrumb(routeSnapshot);

    for (const item of stack) {
      const previous = items[items.length - 1];
      if (item.label && item.routerLink && (item.label !== previous?.label || item.routerLink !== previous?.routerLink)) {
        items.push(item);
      }
    }
    return items;
  }

  private updateBreadcrumbs(): void {
    this.breadcrumbItems = this.buildBreadcrumbs(this.activatedRoute.root.snapshot);
    this.currentPageLabel = this.breadcrumbItems[this.breadcrumbItems.length - 1]?.label || 'لوحة التحكم';
  }

  private collectBreadcrumb(snapshot: any, url = ''): MenuItem[] {
    const items: MenuItem[] = [];
    const path = snapshot.url.map((segment: { path: string }) => segment.path).join('/');
    const nextUrl = `${url}/${path}`.replace(/\/+/g, '/');

    if (snapshot.data?.['breadcrumb'] && snapshot.data['breadcrumb'] !== 'الرئيسية') {
      items.push({
        label: snapshot.data['breadcrumb'],
        routerLink: nextUrl === '/' ? '/dashboard' : nextUrl
      });
    }

    for (const child of snapshot.children || []) {
      items.push(...this.collectBreadcrumb(child, nextUrl));
    }

    return items;
  }

  private checkViewport(): void {
    const viewportWidth = typeof window === 'undefined' ? 0 : window.innerWidth;

    if (viewportWidth >= 1024) {
      this.navigationMode = 'desktop';
      return;
    }

    if (viewportWidth >= 768) {
      this.navigationMode = 'tablet-landscape';
      return;
    }

    this.navigationMode = viewportWidth >= 600 ? 'tablet-portrait' : 'phone';
  }

  private hydrateNavigation(): void {
    this.permissionService.ensureHydrated()
      .pipe(takeUntil(this.destroy$))
      .subscribe({
        error: () => undefined
      });
  }
}
