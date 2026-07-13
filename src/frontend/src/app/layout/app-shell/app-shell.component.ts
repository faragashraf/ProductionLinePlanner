import { Component, HostListener, OnDestroy, OnInit } from '@angular/core';
import { ActivatedRoute, ActivationEnd, NavigationEnd, Router } from '@angular/router';
import { MenuItem } from 'primeng/api';
import { Subject, filter, takeUntil, map } from 'rxjs';
import { APP_NAVIGATION_ITEMS, AppNavigationItem } from '../../core/config/navigation.config';
import { PermissionHydrationState, PermissionService } from '../../core/services/permission.service';
import { AuthService } from '../../core/services/auth.service';

@Component({
  selector: 'app-shell',
  templateUrl: './app-shell.component.html',
  styleUrls: ['./app-shell.component.scss']
})
export class AppShellComponent implements OnInit, OnDestroy {
  isSidebarVisible = false;
  isMobile = false;
  breadcrumbItems: MenuItem[] = [];
  notificationCount = 3;

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

    this.permissionService.ensureHydrated().subscribe();

    this.router.events
      .pipe(
        filter(event => event instanceof NavigationEnd || event instanceof ActivationEnd),
        takeUntil(this.destroy$)
      )
      .subscribe(() => {
        this.breadcrumbItems = this.buildBreadcrumbs(this.activatedRoute.root.snapshot);
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
    if (!this.isMobile) {
      this.isSidebarVisible = false;
    }
  }

  toggleSidebar(): void {
    this.isSidebarVisible = !this.isSidebarVisible;
  }

  closeSidebar(): void {
    this.isSidebarVisible = false;
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
    this.isMobile = window.innerWidth < 992;
  }
}
