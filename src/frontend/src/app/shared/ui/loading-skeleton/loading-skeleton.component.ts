import { ChangeDetectionStrategy, Component, Input } from '@angular/core';

@Component({
  selector: 'plp-loading-skeleton',
  templateUrl: './loading-skeleton.component.html',
  styleUrls: ['./loading-skeleton.component.scss'],
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class LoadingSkeletonComponent {
  @Input() lines = 3;
  @Input() showAvatar = true;
  @Input() cardLike = true;

  get items(): number[] {
    return Array.from({ length: Math.max(1, this.lines) });
  }
}
