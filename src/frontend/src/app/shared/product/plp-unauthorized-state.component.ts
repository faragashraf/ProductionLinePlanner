import { ChangeDetectionStrategy, Component, Input } from '@angular/core';
import { PlpStateShellComponent } from './plp-state-shell.component';

@Component({
  selector: 'plp-product-unauthorized-state',
  standalone: true,
  imports: [PlpStateShellComponent],
  template: `
    <plp-state-shell
      [title]="title"
      [description]="description"
      icon="pi-lock"
      tone="unauthorized"
      role="alert"
    ></plp-state-shell>
  `,
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class PlpProductUnauthorizedStateComponent {
  @Input() title = 'غير مصرح بالوصول';
  @Input() description = 'لا تملك الصلاحية المطلوبة لعرض هذا المحتوى أو تنفيذه.';
}
