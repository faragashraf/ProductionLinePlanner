import { ChangeDetectionStrategy, Component, EventEmitter, Input, Output } from '@angular/core';

@Component({
  selector: 'plp-empty-state',
  templateUrl: './empty-state.component.html',
  styleUrls: ['./empty-state.component.scss'],
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class EmptyStateComponent {
  @Input() title = 'لا توجد بيانات';
  @Input() description = 'لا يوجد محتوى لعرضه في هذه المنطقة حالياً.';
  @Input() icon = 'pi-inbox';
  @Input() actionText = '';
  @Output() action = new EventEmitter<void>();

  emitAction(): void {
    this.action.emit();
  }
}
