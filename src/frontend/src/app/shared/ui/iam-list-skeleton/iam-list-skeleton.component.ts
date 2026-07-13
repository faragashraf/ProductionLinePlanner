import { ChangeDetectionStrategy, Component, Input } from '@angular/core';

@Component({
  selector: 'plp-iam-list-skeleton',
  templateUrl: './iam-list-skeleton.component.html',
  styleUrls: ['./iam-list-skeleton.component.scss'],
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class IamListSkeletonComponent {
  @Input() rows = 4;
  @Input() dense = false;

  get items(): number[] {
    return Array.from({ length: Math.max(1, this.rows) });
  }
}
