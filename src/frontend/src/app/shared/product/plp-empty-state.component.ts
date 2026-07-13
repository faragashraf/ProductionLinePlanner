import { ChangeDetectionStrategy, Component, EventEmitter, Input, Output } from '@angular/core';
import { PlpStateShellComponent } from './plp-state-shell.component';

@Component({
  selector: 'plp-product-empty-state',
  standalone: true,
  imports: [PlpStateShellComponent],
  template: `
    <plp-state-shell
      [title]="title"
      [description]="description"
      [icon]="icon"
      [actionLabel]="actionLabel"
      (action)="action.emit()"
    ></plp-state-shell>
  `,
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class PlpProductEmptyStateComponent {
  @Input() title = 'لا توجد بيانات';
  @Input() description = 'لا يوجد محتوى لعرضه في هذه المنطقة حالياً.';
  @Input() icon = 'pi-inbox';
  @Input() actionLabel = '';
  @Output() action = new EventEmitter<void>();
}
