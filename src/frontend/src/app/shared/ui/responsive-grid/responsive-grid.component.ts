import { ChangeDetectionStrategy, Component, HostBinding, Input } from '@angular/core';

@Component({
  selector: 'plp-responsive-grid',
  templateUrl: './responsive-grid.component.html',
  styleUrls: ['./responsive-grid.component.scss'],
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class ResponsiveGridComponent {
  @Input() minItemWidth = 280;
  @Input() gap = 'var(--plp-spacing-4)';

  @HostBinding('style.--plp-grid-min-width.px') get itemMinWidth(): number {
    return Math.max(220, this.minItemWidth);
  }

  @HostBinding('style.--plp-grid-gap') get gridGap(): string {
    return this.gap;
  }
}
