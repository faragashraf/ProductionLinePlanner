import { NO_ERRORS_SCHEMA } from '@angular/core';
import { ComponentFixture, TestBed, fakeAsync, tick } from '@angular/core/testing';
import { By } from '@angular/platform-browser';
import { ActivatedRoute, NavigationEnd, Router } from '@angular/router';
import { NoopAnimationsModule } from '@angular/platform-browser/animations';
import { Sidebar, SidebarModule } from 'primeng/sidebar';
import { FocusTrap, FocusTrapModule } from 'primeng/focustrap';
import { BehaviorSubject, Subject, of } from 'rxjs';
import { AppNavigationItem } from '../../core/config/navigation.config';
import { AuthService } from '../../core/services/auth.service';
import { PermissionHydrationState, PermissionService } from '../../core/services/permission.service';
import { ProductExperienceModule } from '../../shared/product/product-experience.module';
import { AppShellComponent, ShellNavigationMode } from './app-shell.component';
import { PRODUCT_IDENTITY } from '../../core/config/product-identity.config';

describe('AppShellComponent', () => {
  let fixture: ComponentFixture<AppShellComponent>;
  let component: AppShellComponent;
  let router: RouterStub;
  let permissions: PermissionServiceStub;
  let authService: jasmine.SpyObj<AuthService>;
  let originalInnerWidth: number;

  const navigation: AppNavigationItem[] = [
    { id: 'dashboard', label: 'لوحة التحكم', route: '/dashboard', icon: 'pi-home', order: 10, group: 'workspace' },
    { id: 'workers', label: 'العاملون', route: '/workers', icon: 'pi-users', order: 20, group: 'workspace', permission: 'workers.view' },
    { id: 'users', label: 'إدارة المستخدمين', route: '/admin/users', icon: 'pi-id-card', order: 30, group: 'administration', permission: 'users.view' }
  ];

  beforeEach(() => {
    originalInnerWidth = window.innerWidth;
    setViewport(390);
    router = new RouterStub('/dashboard');
    permissions = new PermissionServiceStub(navigation);
    authService = jasmine.createSpyObj<AuthService>('AuthService', ['logout']);
    Object.defineProperty(authService, 'userName', { get: () => 'مشرف التشغيل' });

    TestBed.configureTestingModule({
      declarations: [AppShellComponent],
      imports: [NoopAnimationsModule, ProductExperienceModule, SidebarModule, FocusTrapModule],
      providers: [
        { provide: Router, useValue: router },
        {
          provide: ActivatedRoute,
          useValue: { root: { snapshot: { url: [], data: {}, children: [] } } }
        },
        { provide: AuthService, useValue: authService },
        { provide: PermissionService, useValue: permissions }
      ],
      schemas: [NO_ERRORS_SCHEMA]
    });

    fixture = TestBed.createComponent(AppShellComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  });

  afterEach(() => {
    setViewport(originalInnerWidth);
  });

  it('renders the existing permission-filtered navigation and preserves the business router outlet', () => {
    const text = fixture.nativeElement.textContent as string;

    expect(permissions.filterNavigation).toHaveBeenCalled();
    expect(text).toContain('لوحة التحكم');
    expect(text).toContain('العاملون');
    expect(text).toContain('إدارة المستخدمين');
    expect(fixture.nativeElement.querySelector('router-outlet')).not.toBeNull();
  });

  it('uses the static shared Flowline mark in the compact header and an Arabic lockup in roomy navigation', () => {
    const topbar = fixture.nativeElement.querySelector('.plp-app-shell__topbar') as HTMLElement;
    const compactLogo = topbar.querySelector('plp-brand-logo [data-plp-brand-variant="header"]') as HTMLElement;

    expect(compactLogo).not.toBeNull();
    expect(compactLogo.classList.contains('plp-brand-logo--animated')).toBeFalse();
    expect(topbar.querySelectorAll(':scope > svg')).toHaveSize(0);

    setViewport(1024);
    component.onResize();
    fixture.detectChanges();
    const lockup = fixture.nativeElement.querySelector('.plp-app-shell__desktop-nav plp-brand-logo [data-plp-brand-variant="horizontal"]') as HTMLElement;

    expect(lockup).not.toBeNull();
    expect(lockup.textContent).toContain(PRODUCT_IDENTITY.nameAr);
  });

  it('renders the centralized static product identity across the enterprise shell', () => {
    const text = fixture.nativeElement.textContent as string;
    const navbarName = fixture.nativeElement.querySelector('.plp-app-shell__brand-name') as HTMLElement;

    expect(navbarName.textContent?.trim()).toBe(PRODUCT_IDENTITY.nameEn);
    expect(navbarName.textContent).not.toContain(PRODUCT_IDENTITY.nameAr);
    expect(text).toContain(PRODUCT_IDENTITY.nameEn);
    expect(text).toContain(PRODUCT_IDENTITY.platformLabelAr);
    expect(text).toContain(PRODUCT_IDENTITY.workspaceNameAr);
  });

  it('keeps permission-filtered navigation group references stable between render passes', () => {
    const workspaceItems = component.workspaceNavigationItems;
    const administrationItems = component.administrationNavigationItems;

    fixture.detectChanges();
    fixture.detectChanges();

    expect(component.workspaceNavigationItems).toBe(workspaceItems);
    expect(component.administrationNavigationItems).toBe(administrationItems);
  });

  it('renders the active navigation state from the existing route contract', () => {
    const activeLink = fixture.nativeElement.querySelector('.plp-app-shell__nav-link--active') as HTMLAnchorElement;

    expect(activeLink.textContent).toContain('لوحة التحكم');
    expect(activeLink.getAttribute('aria-current')).toBe('page');
  });

  it('renders the phone menu trigger, opens the mobile Sidebar, exposes expanded state, and closes from the close action', fakeAsync(() => {
    const menuButton = fixture.nativeElement.querySelector('[aria-label="فتح القائمة"]') as HTMLButtonElement;

    expect(menuButton).not.toBeNull();
    expect(menuButton.getAttribute('aria-expanded')).toBe('false');

    menuButton.click();
    tick(200);
    fixture.detectChanges();
    expect(component.sidebarOpen).toBeTrue();
    expect(menuButton.getAttribute('aria-expanded')).toBe('true');
    expect(getOverlaySidebar().container).not.toBeNull();

    const closeButton = fixture.nativeElement.querySelector('[aria-label="إغلاق القائمة"]') as HTMLButtonElement;
    closeButton.click();
    tick(200);
    fixture.detectChanges();

    expect(component.sidebarOpen).toBeFalse();
    expect(menuButton.getAttribute('aria-expanded')).toBe('false');
  }));

  it('uses the body-overlay RTL/right Sidebar contract in phone and tablet portrait modes', fakeAsync(() => {
    openOverlayDrawer();
    tick(200);
    fixture.detectChanges();

    let sidebar = getOverlaySidebar();
    const drawer = sidebar.container as HTMLElement;
    const sidebarIdentity = drawer.querySelector('.plp-app-shell__sidebar-identity') as HTMLElement;
    expect(sidebar.position).toBe('right');
    expect(sidebar.styleClass).toBe('plp-app-shell-overlay-nav');
    expect(drawer.classList.contains('p-sidebar')).toBeTrue();
    expect(drawer.classList.contains('plp-app-shell-overlay-nav')).toBeTrue();
    expect(sidebarIdentity.textContent).toContain(PRODUCT_IDENTITY.nameAr);
    expect(sidebarIdentity.textContent).not.toContain(PRODUCT_IDENTITY.nameEn);
    expect(component.navigationMode).toBe('phone');

    setViewport(600);
    component.onResize();
    fixture.detectChanges();
    expect(component.navigationMode).toBe('tablet-portrait');
    expect(component.isOverlayNavigation).toBeTrue();
    expect(fixture.nativeElement.querySelector('[aria-label="فتح القائمة"]')).not.toBeNull();
    sidebar = getOverlaySidebar();
    expect(sidebar).toBeTruthy();

    component.closeSidebar();
    fixture.detectChanges();
    tick(200);
  }));

  it('renders a dismissible backdrop below the body-appended drawer and closes when the mask is clicked', fakeAsync(() => {
    openOverlayDrawer();
    tick(200);
    fixture.detectChanges();

    const sidebar = getOverlaySidebar();
    const drawer = sidebar.container as HTMLElement;
    const mask = sidebar.mask as HTMLElement;

    expect(drawer).not.toBeNull();
    expect(mask).not.toBeNull();
    expect(Number(drawer.style.zIndex)).toBeGreaterThan(Number(mask.style.zIndex));

    mask.click();
    tick(200);
    fixture.detectChanges();

    expect(component.sidebarOpen).toBeFalse();
  }));

  it('keeps keyboard focus inside the open overlay drawer and restores it to the trigger after close', fakeAsync(() => {
    openOverlayDrawer();
    tick(200);
    fixture.detectChanges();

    const focusTrap = fixture.debugElement.query(By.directive(FocusTrap));
    const focusTrapElement = focusTrap.nativeElement as HTMLElement;
    const firstSentinel = focusTrapElement.querySelector('[data-pc-section="firstfocusableelement"]') as HTMLElement;
    const lastSentinel = focusTrapElement.querySelector('[data-pc-section="lastfocusableelement"]') as HTMLElement;
    const closeButton = focusTrapElement.querySelector('[aria-label="إغلاق القائمة"]') as HTMLButtonElement;
    const focusTrapDirective = focusTrap.injector.get(FocusTrap);

    expect(focusTrap).not.toBeNull();
    expect(focusTrapDirective.pFocusTrapDisabled).toBeFalse();
    expect(firstSentinel).not.toBeNull();
    expect(lastSentinel).not.toBeNull();

    closeButton.focus();
    expect(document.activeElement).toBe(closeButton);

    component.closeSidebar();
    component.onOverlayNavigationHide();
    tick(200);
    fixture.detectChanges();

    expect(document.activeElement).toBe(fixture.nativeElement.querySelector('[aria-label="فتح القائمة"]'));
  }));

  it('keeps the close action and menu link clickable while the backdrop is present', fakeAsync(() => {
    openOverlayDrawer();
    tick(200);
    fixture.detectChanges();

    const sidebar = getOverlaySidebar();
    const mask = sidebar.mask as HTMLElement;
    const closeButton = sidebar.container?.querySelector('[aria-label="إغلاق القائمة"]') as HTMLButtonElement;
    expect(mask).not.toBeNull();
    expect(closeButton).not.toBeNull();

    closeButton.click();
    tick(200);
    fixture.detectChanges();
    expect(component.sidebarOpen).toBeFalse();

    openOverlayDrawer();
    tick(200);
    fixture.detectChanges();
    const menuItem = getOverlaySidebar().container?.querySelector('.plp-app-shell__nav-link') as HTMLAnchorElement;
    expect(menuItem).not.toBeNull();

    menuItem.click();
    tick(200);
    fixture.detectChanges();
    expect(component.sidebarOpen).toBeFalse();
  }));

  it('closes the overlay drawer on Escape', fakeAsync(() => {
    openOverlayDrawer();

    document.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape' }));
    fixture.detectChanges();
    tick(200);

    expect(component.sidebarOpen).toBeFalse();
  }));

  it('does not reopen from PrimeNG’s show lifecycle after the App Shell closes it', () => {
    component.sidebarOpen = false;
    component.onSidebarVisibleChange(true);

    expect(component.sidebarOpen).toBeFalse();
  });

  it('closes the overlay drawer after route navigation on phone and tablet', fakeAsync(() => {
    openOverlayDrawer();

    router.eventsSubject.next(new NavigationEnd(1, '/workers', '/workers'));
    fixture.detectChanges();
    tick(200);
    expect(component.sidebarOpen).toBeFalse();

    setViewport(700);
    component.onResize();
    openOverlayDrawer();
    router.eventsSubject.next(new NavigationEnd(2, '/workers', '/workers'));
    fixture.detectChanges();
    tick(200);
    expect(component.sidebarOpen).toBeFalse();
  }));

  it('uses overlay navigation for tablet portrait and compact persistent navigation for tablet landscape', () => {
    expect(component.navigationMode).toBe('phone');

    setViewport(700);
    component.onResize();
    expect(component.navigationMode).toBe('tablet-portrait');
    expect(component.isOverlayNavigation).toBeTrue();

    setViewport(800);
    component.onResize();
    fixture.detectChanges();
    expect(component.navigationMode).toBe('tablet-landscape');
    expect(component.hasPersistentNavigation).toBeTrue();
    expect(fixture.nativeElement.querySelector('.plp-app-shell__desktop-nav')).not.toBeNull();
    expect(fixture.nativeElement.querySelector('[aria-label="فتح القائمة"]')).toBeNull();

    setViewport(1280);
    component.onResize();
    expect(component.navigationMode).toBe('desktop');
    expect(component.isOverlayNavigation).toBeFalse();
  });

  it('keeps the desktop sidebar persistent without an overlay trigger', () => {
    setViewport(1280);
    component.onResize();
    fixture.detectChanges();

    expect(fixture.nativeElement.querySelector('.plp-app-shell__desktop-nav')).not.toBeNull();
    expect(fixture.nativeElement.querySelector('[aria-label="فتح القائمة"]')).toBeNull();
    expect(component.isOverlayNavigation).toBeFalse();
  });

  it('keeps scrolling inside page content and persistent navigation across tablet and desktop modes', () => {
    const scenarios: Array<[number, ShellNavigationMode, boolean]> = [
      [700, 'tablet-portrait', false],
      [800, 'tablet-landscape', true],
      [1280, 'desktop', true]
    ];

    for (const [width, expectedMode, hasPersistentSidebar] of scenarios) {
      setViewport(width);
      component.onResize();
      fixture.detectChanges();

      const shell = fixture.nativeElement.querySelector('.plp-app-shell') as HTMLElement;
      const main = fixture.nativeElement.querySelector('.plp-app-shell__main') as HTMLElement;
      const sidebar = fixture.nativeElement.querySelector('.plp-app-shell__desktop-nav') as HTMLElement;
      const shellStyle = window.getComputedStyle(shell);
      const mainStyle = window.getComputedStyle(main);
      const sidebarStyle = window.getComputedStyle(sidebar);

      expect(component.navigationMode).withContext(`width ${width}`).toBe(expectedMode);
      expect(shellStyle.overflowY).withContext(`shell width ${width}`).toBe('hidden');
      expect(mainStyle.overflowY).withContext(`content width ${width}`).toBe('auto');
      expect(sidebarStyle.position).withContext(`sidebar width ${width}`).not.toBe('fixed');

      if (hasPersistentSidebar) {
        expect(sidebarStyle.display).withContext(`sidebar width ${width}`).toBe('block');
        expect(sidebarStyle.overflowY).withContext(`sidebar width ${width}`).toBe('auto');
        expect(fixture.nativeElement.querySelector('[aria-label="فتح القائمة"]')).toBeNull();
      } else {
        expect(sidebarStyle.display).withContext(`sidebar width ${width}`).toBe('none');
        expect(fixture.debugElement.query(By.directive(Sidebar))).not.toBeNull();
        expect(fixture.nativeElement.querySelector('[aria-label="فتح القائمة"]')).not.toBeNull();
      }
    }
  });

  it('keeps a navigation mode available at every required breakpoint boundary', () => {
    const expectedModes: Array<[number, ShellNavigationMode]> = [
      [320, 'phone'],
      [360, 'phone'],
      [390, 'phone'],
      [599, 'phone'],
      [600, 'tablet-portrait'],
      [767, 'tablet-portrait'],
      [768, 'tablet-landscape'],
      [1023, 'tablet-landscape'],
      [1024, 'desktop'],
      [1280, 'desktop']
    ];

    for (const [width, expectedMode] of expectedModes) {
      setViewport(width);
      component.onResize();

      expect(component.navigationMode).withContext(`width ${width}`).toBe(expectedMode);
      expect(component.isOverlayNavigation || component.hasPersistentNavigation)
        .withContext(`width ${width}`)
        .toBeTrue();
    }
  });

  it('keeps the header sticky with a logical block-start inset', () => {
    const topbar = fixture.nativeElement.querySelector('.plp-app-shell__topbar') as HTMLElement;
    const computedStyle = window.getComputedStyle(topbar);

    expect(computedStyle.position).toBe('sticky');
    expect(computedStyle.insetBlockStart).toBe('0px');
  });

  it('continues to filter menu items through the existing permission service', () => {
    permissions.filterNavigation.and.returnValue([navigation[0]]);
    permissions.permissions$.next(['dashboard.view']);
    fixture.detectChanges();

    const text = fixture.nativeElement.textContent as string;
    expect(text).toContain('لوحة التحكم');
    expect(text).not.toContain('العاملون');
    expect(text).not.toContain('إدارة المستخدمين');
  });

  it('uses the existing logout action and destination without changing permissions or routes', () => {
    component.logout();

    expect(authService.logout).toHaveBeenCalledTimes(1);
    expect(router.navigateByUrl).toHaveBeenCalledOnceWith('/login');
  });

  function setViewport(width: number): void {
    Object.defineProperty(window, 'innerWidth', { configurable: true, value: width });
  }

  function openOverlayDrawer(): void {
    const menuButton = fixture.nativeElement.querySelector('[aria-label="فتح القائمة"]') as HTMLButtonElement;
    menuButton.click();
    fixture.detectChanges();
  }

  function getOverlaySidebar(): Sidebar {
    return fixture.debugElement.query(By.directive(Sidebar)).componentInstance as Sidebar;
  }
});

class RouterStub {
  readonly eventsSubject = new Subject<unknown>();
  readonly events = this.eventsSubject.asObservable();
  readonly navigateByUrl = jasmine.createSpy('navigateByUrl');

  constructor(public url: string) {}
}

class PermissionServiceStub {
  readonly permissions$ = new BehaviorSubject<string[]>([]);
  readonly hydrationState$ = new BehaviorSubject<PermissionHydrationState>('ready');
  readonly filterNavigation = jasmine.createSpy('filterNavigation').and.callFake(() => this.navigation);
  readonly ensureHydrated = jasmine.createSpy('ensureHydrated').and.returnValue(of([]));

  constructor(private readonly navigation: AppNavigationItem[]) {}
}
