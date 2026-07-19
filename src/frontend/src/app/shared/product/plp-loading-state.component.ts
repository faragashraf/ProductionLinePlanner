import { ChangeDetectionStrategy, Component, Input } from '@angular/core';
import { CommonModule } from '@angular/common';
import { SkeletonModule } from 'primeng/skeleton';

@Component({
  selector: 'plp-product-loading-state',
  standalone: true,
  imports: [CommonModule, SkeletonModule],
  template: `
    <section
      class="plp-product-loading"
      [class.plp-product-loading--card]="cardLike"
      role="status"
      [attr.aria-label]="label"
      [attr.aria-busy]="true"
    >
      <span class="plp-sr-only">{{ label }}</span>
      <article *ngFor="let row of rows" class="plp-product-loading__row" [class.plp-product-loading__row--with-avatar]="showAvatar">
        <p-skeleton *ngIf="showAvatar" shape="circle" size="2.5rem"></p-skeleton>
        <span class="plp-product-loading__body">
          <p-skeleton width="34%" height="0.75rem"></p-skeleton>
          <p-skeleton width="68%" height="0.625rem"></p-skeleton>
          <p-skeleton width="92%" height="0.625rem"></p-skeleton>
        </span>
      </article>
    </section>
  `,
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class PlpProductLoadingStateComponent {
  private lineCount = 4;

  readonly defaultRows = [0, 1, 2, 3];
  rows: readonly number[] = this.defaultRows;

  @Input()
  set lines(value: number) {
    const normalized = Math.max(1, Math.trunc(Number(value) || 1));
    if (normalized === this.lineCount) {
      return;
    }

    this.lineCount = normalized;
    this.rows = Array.from({ length: normalized }, (_, index) => index);
  }

  get lines(): number {
    return this.lineCount;
  }

  @Input() label = 'جارٍ التحميل';
  @Input() showAvatar = false;
  @Input() cardLike = false;
}
