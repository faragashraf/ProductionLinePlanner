import { ChangeDetectionStrategy, Component, Input } from '@angular/core';
import { CommonModule } from '@angular/common';
import { SkeletonModule } from 'primeng/skeleton';

@Component({
  selector: 'plp-product-loading-state',
  standalone: true,
  imports: [CommonModule, SkeletonModule],
  template: `
    <section class="plp-product-loading" role="status" [attr.aria-label]="label" [attr.aria-busy]="true">
      <span class="plp-sr-only">{{ label }}</span>
      <p-skeleton *ngFor="let row of rows" width="100%" height="var(--plp-control-height-standard)"></p-skeleton>
    </section>
  `,
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class PlpProductLoadingStateComponent {
  @Input() lines = 4;
  @Input() label = 'جارٍ التحميل';

  get rows(): readonly number[] {
    return Array.from({ length: Math.max(1, this.lines) }, (_, index) => index);
  }
}
