import { Component, ElementRef, HostListener, OnDestroy, OnInit, ViewChild } from '@angular/core';
import { ActivatedRoute, ActivationEnd, NavigationEnd, Router } from '@angular/router';
import { MenuItem } from 'primeng/api';
import { Subject, filter, takeUntil, map } from 'rxjs';
import { APP_NAVIGATION_ITEMS, AppNavigationItem } from '../../core/config/navigation.config';
import { PermissionHydrationState, PermissionService } from '../../core/services/permission.service';
import { AuthService } from '../../core/services/auth.service';
import { PRODUCTION_RUNTIME_Z_INDEX } from '../../shared/design-system/layering/production-z-index';
import { PLP_ANGULAR_MOTION } from '../../shared/product/product-motion';

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
  navigationMode: ShellNavigationMode = 'phone';
  breadcrumbItems: MenuItem[] = [];
  readonly overlaySidebarBaseZIndex = PRODUCTION_RUNTIME_Z_INDEX.modal;
  readonly sidebarTransitionOptions = PLP_ANGULAR_MOTION.sidebar;

  navigationItems: AppNavigationItem[] = [];
  permissionHydrationState: PermissionHydrationState = 'idle';

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
      });

    this.hydrateNavigation();

    this.router.events
      .pipe(
        filter(event => event instanceof NavigationEnd || event instanceof ActivationEnd),
        takeUntil(this.destroy$)
      )
      .subscribe((event) => {
        this.breadcrumbItems = this.buildBreadcrumbs(this.activatedRoute.root.snapshot);
        if (event instanceof NavigationEnd && this.isOverlayNavigation) {
          this.closeSidebar();
        }
      });

    this.breadcrumbItems = this.buildBreadcrumbs(this.activatedRoute.root.snapshot);
  }

  ngOnDestroy(): void {
    this.destroy$.next();
    this.destroy$.complete();
  }

  @HostListener('window:resize')
  onResize(): void {
    this.checkViewport();
    if (!this.isOverlayNavigation) {
      this.closeSidebar();
    }
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
    }
  }

  closeSidebar(): void {
    this.sidebarOpen = false;
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
    return this.router.url.startsWith(path);
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
    return this.navigationMode === 'tablet-landscape' || this.navigationMode === 'desktop';
  }

  get workspaceNavigationItems(): AppNavigationItem[] {
    return this.navigationItems.filter((item) => item.group === 'workspace');
  }

  get administrationNavigationItems(): AppNavigationItem[] {
    return this.navigationItems.filter((item) => item.group === 'administration');
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
      if (item.label && item.routerLink) {
        items.push(item);
      }
    }
    return items;
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
