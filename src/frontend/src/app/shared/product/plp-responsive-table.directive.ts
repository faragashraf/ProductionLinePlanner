import { AfterViewInit, Directive, ElementRef, Inject, Input, NgZone, OnDestroy, PLATFORM_ID, booleanAttribute } from '@angular/core';
import { isPlatformBrowser } from '@angular/common';
import { PlpHorizontalOverflowController } from './plp-horizontal-overflow';

export type PlpTablePresentation = 'scroll' | 'stack';

/**
 * Applies the Product Experience Framework's viewport-safe table container to
 * a PrimeNG table without duplicating column or row templates. Use `scroll`
 * for operational comparison tables and `stack` only when each cell provides
 * an explicit mobile field label.
 */
@Directive({
  selector: 'p-table[plpResponsiveTable]',
  standalone: true,
  host: {
    class: 'plp-operational-table',
    '[class.plp-operational-table--scroll]': "plpResponsiveTable === 'scroll'",
    '[class.plp-operational-table--stack]': "plpResponsiveTable === 'stack'",
    '[class.plp-operational-table--sticky-actions]': 'plpStickyActions',
    '[attr.data-plp-table-presentation]': 'plpResponsiveTable'
  }
})
export class PlpResponsiveTableDirective implements AfterViewInit, OnDestroy {
  @Input() plpResponsiveTable: PlpTablePresentation = 'scroll';
  @Input({ transform: booleanAttribute }) plpStickyActions = false;

  private controller: PlpHorizontalOverflowController | null = null;
  private destroyed = false;

  constructor(
    private readonly elementRef: ElementRef<HTMLElement>,
    @Inject(PLATFORM_ID) private readonly platformId: object,
    private readonly ngZone: NgZone
  ) {}

  ngAfterViewInit(): void {
    if (!isPlatformBrowser(this.platformId) || this.plpResponsiveTable !== 'scroll') {
      return;
    }

    // Geometry observers mutate only presentation affordances. Keeping their
    // callbacks outside Angular prevents DOM character-data observations from
    // recursively scheduling application-wide change detection.
    queueMicrotask(() => {
      if (this.destroyed) {
        return;
      }

      this.ngZone.runOutsideAngular(() => this.attachOverflowObserver());
    });
  }

  ngOnDestroy(): void {
    this.destroyed = true;
    this.controller?.destroy();
    this.controller = null;
  }

  private attachOverflowObserver(): void {
    const host = this.elementRef.nativeElement;
    const wrapper = host.querySelector<HTMLElement>('.p-datatable-wrapper');
    if (!wrapper) {
      return;
    }

    this.controller = new PlpHorizontalOverflowController();
    this.controller.attach(wrapper, host, {
      contentElements: () => {
        const table = wrapper.querySelector('table');
        return table ? [table] : [];
      },
      hint: 'اسحب لعرض المزيد',
      hintSide: this.plpStickyActions ? 'start' : 'auto'
    });
  }
}
