import { AfterViewInit, Directive, ElementRef, Inject, Input, NgZone, OnDestroy, PLATFORM_ID } from '@angular/core';
import { isPlatformBrowser } from '@angular/common';
import { ActivatedRoute, Router } from '@angular/router';
import { Subject, takeUntil } from 'rxjs';

/**
 * Keeps fragment navigation inside one explicitly bounded scroll region.
 * Sections opt in with `data-plp-section="summary"`; this directive never
 * calls document scrolling or native anchor navigation.
 */
@Directive({
  selector: '[plpSectionNavigation]',
  standalone: true
})
export class PlpSectionNavigationDirective implements AfterViewInit, OnDestroy {
  @Input() plpSectionNavigationSectionSelector = '[data-plp-section]';
  @Input() plpSectionNavigationEnabled = true;

  private observer: IntersectionObserver | null = null;
  private readonly destroy$ = new Subject<void>();
  private activeId = '';
  private suppressFragmentSync = false;
  private suppressTimer: ReturnType<typeof setTimeout> | null = null;

  constructor(
    private readonly host: ElementRef<HTMLElement>,
    private readonly router: Router,
    private readonly route: ActivatedRoute,
    private readonly zone: NgZone,
    @Inject(PLATFORM_ID) private readonly platformId: object
  ) {}

  ngAfterViewInit(): void {
    if (!this.plpSectionNavigationEnabled || !isPlatformBrowser(this.platformId)) {
      return;
    }

    this.zone.runOutsideAngular(() => this.observeSections());
    this.route.fragment.pipe(takeUntil(this.destroy$)).subscribe(fragment => {
      if (!fragment || this.suppressFragmentSync) return;
      this.navigateTo(fragment, false);
    });
  }

  ngOnDestroy(): void {
    this.observer?.disconnect();
    if (this.suppressTimer) clearTimeout(this.suppressTimer);
    this.destroy$.next();
    this.destroy$.complete();
  }

  navigateTo(sectionId: string, updateFragment = true): boolean {
    const root = this.host.nativeElement;
    const target = this.sections().find(section => section.dataset['plpSection'] === sectionId);
    if (!target) return false;

    const rootBounds = root.getBoundingClientRect();
    const targetBounds = target.getBoundingClientRect();
    const nextTop = Math.max(0, targetBounds.top - rootBounds.top + root.scrollTop);
    this.suppress();
    root.scrollTo({ top: nextTop, behavior: this.prefersReducedMotion ? 'auto' : 'smooth' });
    this.setActive(sectionId, updateFragment);
    return true;
  }

  private observeSections(): void {
    const root = this.host.nativeElement;
    const sections = this.sections();
    if (!sections.length || typeof IntersectionObserver === 'undefined') return;

    this.observer = new IntersectionObserver(entries => {
      if (this.suppressFragmentSync) return;
      const visible = entries
        .filter(entry => entry.isIntersecting)
        .sort((left, right) => right.intersectionRatio - left.intersectionRatio)[0];
      const id = visible?.target instanceof HTMLElement ? visible.target.dataset['plpSection'] : '';
      if (id) this.setActive(id, true);
    }, { root, rootMargin: '0px 0px -35% 0px', threshold: [0.2, 0.5, 0.8] });

    sections.forEach(section => this.observer?.observe(section));
  }

  private sections(): HTMLElement[] {
    return Array.from(this.host.nativeElement.querySelectorAll<HTMLElement>(this.plpSectionNavigationSectionSelector));
  }

  private setActive(sectionId: string, updateFragment: boolean): void {
    if (sectionId === this.activeId) return;
    this.activeId = sectionId;
    if (!updateFragment) return;
    this.zone.run(() => {
      void this.router.navigate([], {
        relativeTo: this.route,
        fragment: sectionId,
        queryParamsHandling: 'preserve',
        replaceUrl: true
      });
    });
  }

  private suppress(): void {
    this.suppressFragmentSync = true;
    if (this.suppressTimer) clearTimeout(this.suppressTimer);
    this.suppressTimer = setTimeout(() => this.suppressFragmentSync = false, 450);
  }

  private get prefersReducedMotion(): boolean {
    return typeof window !== 'undefined' && window.matchMedia('(prefers-reduced-motion: reduce)').matches;
  }
}
