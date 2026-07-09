import { ChangeDetectionStrategy, Component, Input } from '@angular/core';

@Component({
  selector: 'plp-page-header',
  templateUrl: './page-header.component.html',
  styleUrls: ['./page-header.component.scss'],
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class PageHeaderComponent {
  @Input() title = '';
  @Input() description = '';
  @Input() compact = false;
}
