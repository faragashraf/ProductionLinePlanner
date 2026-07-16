import { AfterViewInit, Directive, ElementRef, Inject, Input, OnChanges, OnDestroy, PLATFORM_ID, SimpleChanges } from '@angular/core';
import { isPlatformBrowser } from '@angular/common';

export type PlpHintSide = 'auto' | 'start' | 'end';

interface PlpHorizontalOverflowOptions {
  contentElements: () => readonly Element[];
  hint?: string;
  hintSide?: PlpHintSide;
}

/**
 * One RTL-safe scroll-state observer for all horizontal operational rails.
 * It intentionally derives the hidden edges from layout geometry instead of
 * browser-specific RTL scrollLeft values, which differ between WebKit and
 * Chromium implementations.
 */
export class PlpHorizontalOverflowController {
  private scroller: HTMLElement | null = null;
  private presentation: HTMLElement | null = null;
  private options: PlpHorizontalOverflowOptions | null = null;
  private resizeObserver: ResizeObserver | null = null;
  private mutationObserver: MutationObserver | null = null;
  private resizeFrame = 0;
  private interacted = false;
  private hadOverflow = false;
  private readonly startEdge: HTMLSpanElement;
  private readonly endEdge: HTMLSpanElement;
  private readonly hint: HTMLSpanElement;

  constructor(private readonly onContentsChanged?: () => void) {
    this.startEdge = this.createAffordance('plp-scroll-edge plp-scroll-edge--inline-start');
    this.endEdge = this.createAffordance('plp-scroll-edge plp-scroll-edge--inline-end');
    this.hint = this.createAffordance('plp-scroll-hint');
    this.hint.textContent = 'اسحب لعرض المزيد';
  }

  attach(scroller: HTMLElement, presentation: HTMLElement, options: PlpHorizontalOverflowOptions): void {
    this.destroy();

    this.scroller = scroller;
    this.presentation = presentation;
    this.options = options;
    this.interacted = false;
    this.hadOverflow = false;

    presentation.classList.add('plp-horizontal-overflow');
    presentation.append(this.startEdge, this.endEdge, this.hint);
    scroller.classList.add('plp-horizontal-scroll-target');

    if (!scroller.hasAttribute('tabindex')) {
      scroller.tabIndex = 0;
    }
    if (!scroller.hasAttribute('aria-label')) {
      scroller.setAttribute('aria-label', 'منطقة قابلة للتمرير أفقيًا');
    }

    scroller.addEventListener('scroll', this.onScroll, { passive: true });
    scroller.addEventListener('pointerdown', this.onInteraction, { passive: true });
    scroller.addEventListener('wheel', this.onInteraction, { passive: true });
    scroller.addEventListener('keydown', this.onKeyDown);

    this.resizeObserver = new ResizeObserver(() => this.scheduleRefresh());
    this.resizeObserver.observe(scroller);

    this.mutationObserver = new MutationObserver(() => {
      this.onContentsChanged?.();
      this.scheduleRefresh();
    });
    this.mutationObserver.observe(scroller, { childList: true, subtree: true, characterData: true });

    this.scheduleRefresh();
  }

  reveal(element: Element | null, smooth = true): void {
    if (!element || !this.scroller) {
      return;
    }

    const viewport = this.scroller.getBoundingClientRect();
    const target = element.getBoundingClientRect();
    const delta = target.left < viewport.left
      ? target.left - viewport.left
      : target.right > viewport.right
        ? target.right - viewport.right
        : 0;
    if (delta) {
      this.scroller.scrollBy({
        left: delta,
        behavior: smooth && !this.prefersReducedMotion ? 'smooth' : 'auto'
      });
    }
    this.scheduleRefresh();
  }

  refresh(): void {
    const scroller = this.scroller;
    const presentation = this.presentation;
    const options = this.options;
    if (!scroller || !presentation || !options) {
      return;
    }

    const hasOverflow = scroller.scrollWidth - scroller.clientWidth > 1;
    if (!hasOverflow) {
      this.interacted = false;
    }

    const { canInlineStart, canInlineEnd } = hasOverflow
      ? this.hiddenEdges(scroller, options.contentElements())
      : { canInlineStart: false, canInlineEnd: false };

    presentation.classList.toggle('plp-horizontal-overflow--active', hasOverflow);
    presentation.classList.toggle('plp-horizontal-overflow--has-inline-start', canInlineStart);
    presentation.classList.toggle('plp-horizontal-overflow--has-inline-end', canInlineEnd);
    presentation.classList.toggle('plp-horizontal-overflow--interacted', this.interacted);

    const shouldShowHint = hasOverflow && !this.interacted && Boolean(options.hint);
    this.hint.textContent = options.hint || '';
    this.hint.hidden = !shouldShowHint;
    this.hint.classList.toggle('plp-scroll-hint--inline-start', this.hintSide(canInlineStart, canInlineEnd) === 'start');
    this.hint.classList.toggle('plp-scroll-hint--inline-end', this.hintSide(canInlineStart, canInlineEnd) === 'end');
    this.hint.setAttribute('aria-hidden', 'true');

    this.hadOverflow = hasOverflow;
  }

  destroy(): void {
    if (this.resizeFrame) {
      cancelAnimationFrame(this.resizeFrame);
      this.resizeFrame = 0;
    }

    this.scroller?.removeEventListener('scroll', this.onScroll);
    this.scroller?.removeEventListener('pointerdown', this.onInteraction);
    this.scroller?.removeEventListener('wheel', this.onInteraction);
    this.scroller?.removeEventListener('keydown', this.onKeyDown);
    this.resizeObserver?.disconnect();
    this.mutationObserver?.disconnect();

    this.presentation?.classList.remove(
      'plp-horizontal-overflow',
      'plp-horizontal-overflow--active',
      'plp-horizontal-overflow--has-inline-start',
      'plp-horizontal-overflow--has-inline-end',
      'plp-horizontal-overflow--interacted'
    );
    this.startEdge.remove();
    this.endEdge.remove();
    this.hint.remove();
    this.scroller?.classList.remove('plp-horizontal-scroll-target');

    this.scroller = null;
    this.presentation = null;
    this.options = null;
    this.resizeObserver = null;
    this.mutationObserver = null;
  }

  private readonly onScroll = (): void => {
    this.markInteracted();
    this.scheduleRefresh();
  };

  private readonly onInteraction = (): void => this.markInteracted();

  private readonly onKeyDown = (event: KeyboardEvent): void => {
    if (!this.scroller || (event.key !== 'ArrowLeft' && event.key !== 'ArrowRight')) {
      return;
    }

    if (event.target !== this.scroller) {
      return;
    }

    event.preventDefault();
    this.markInteracted();
    this.scroller.scrollBy({
      left: event.key === 'ArrowLeft' ? -Math.max(this.scroller.clientWidth * 0.7, 120) : Math.max(this.scroller.clientWidth * 0.7, 120),
      behavior: this.prefersReducedMotion ? 'auto' : 'smooth'
    });
  };

  private markInteracted(): void {
    if (!this.hadOverflow || this.interacted) {
      return;
    }

    this.interacted = true;
    this.scheduleRefresh();
  }

  private scheduleRefresh(): void {
    if (this.resizeFrame) {
      return;
    }

    this.resizeFrame = requestAnimationFrame(() => {
      this.resizeFrame = 0;
      this.refresh();
    });
  }

  private hiddenEdges(scroller: HTMLElement, contentElements: readonly Element[]): { canInlineStart: boolean; canInlineEnd: boolean } {
    const rects = contentElements
      .map(element => element.getBoundingClientRect())
      .filter(rect => rect.width > 0);
    if (rects.length === 0) {
      return { canInlineStart: false, canInlineEnd: false };
    }

    const viewport = scroller.getBoundingClientRect();
    const contentLeft = Math.min(...rects.map(rect => rect.left));
    const contentRight = Math.max(...rects.map(rect => rect.right));
    const tolerance = 2;
    const isRtl = getComputedStyle(scroller).direction === 'rtl';

    return isRtl
      ? {
        canInlineStart: contentRight > viewport.right + tolerance,
        canInlineEnd: contentLeft < viewport.left - tolerance
      }
      : {
        canInlineStart: contentLeft < viewport.left - tolerance,
        canInlineEnd: contentRight > viewport.right + tolerance
      };
  }

  private hintSide(canInlineStart: boolean, canInlineEnd: boolean): Exclude<PlpHintSide, 'auto'> {
    const preferred = this.options?.hintSide ?? 'auto';
    if (preferred !== 'auto') {
      return preferred;
    }
    if (canInlineStart && !canInlineEnd) {
      return 'start';
    }
    return 'end';
  }

  private get prefersReducedMotion(): boolean {
    return typeof window !== 'undefined' && window.matchMedia('(prefers-reduced-motion: reduce)').matches;
  }

  private createAffordance(className: string): HTMLSpanElement {
    const element = document.createElement('span');
    element.className = className;
    return element;
  }
}

/** A generic, permission-safe navigation rail with active-item reachability. */
@Directive({
  selector: '[plpOverflowRail]',
  standalone: true,
  host: {
    class: 'plp-overflow-rail'
  }
})
export class PlpOverflowRailDirective implements AfterViewInit, OnChanges, OnDestroy {
  @Input() plpOverflowRailActiveId = '';
  @Input() plpOverflowRailItemSelector = '[data-plp-overflow-rail-item]';
  @Input() plpOverflowRailPresentation: HTMLElement | null = null;

  private controller: PlpHorizontalOverflowController | null = null;

  constructor(
    private readonly elementRef: ElementRef<HTMLElement>,
    @Inject(PLATFORM_ID) private readonly platformId: object
  ) {}

  ngAfterViewInit(): void {
    if (!isPlatformBrowser(this.platformId)) {
      return;
    }

    this.controller = new PlpHorizontalOverflowController(() => this.revealActiveItem());
    this.controller.attach(this.elementRef.nativeElement, this.plpOverflowRailPresentation ?? this.elementRef.nativeElement.parentElement ?? this.elementRef.nativeElement, {
      contentElements: () => Array.from(this.elementRef.nativeElement.querySelectorAll(this.plpOverflowRailItemSelector))
    });
    this.revealActiveItem();
    window.addEventListener('resize', this.onViewportResize, { passive: true });
  }

  ngOnChanges(_: SimpleChanges): void {
    this.revealActiveItem();
  }

  ngOnDestroy(): void {
    window.removeEventListener('resize', this.onViewportResize);
    this.controller?.destroy();
  }

  private readonly onViewportResize = (): void => this.revealActiveItem();

  private revealActiveItem(): void {
    if (!this.controller || !this.plpOverflowRailActiveId) {
      return;
    }

    queueMicrotask(() => {
      const item = Array.from(this.elementRef.nativeElement.querySelectorAll<HTMLElement>(this.plpOverflowRailItemSelector))
        .find(element => element.dataset['plpOverflowRailItem'] === this.plpOverflowRailActiveId);
      this.controller?.reveal(item ?? null);
    });
  }
}
