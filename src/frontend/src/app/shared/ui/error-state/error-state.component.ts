import { ChangeDetectionStrategy, Component, EventEmitter, Input, Output } from '@angular/core';

@Component({
  selector: 'plp-error-state',
  templateUrl: './error-state.component.html',
  styleUrls: ['./error-state.component.scss'],
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class ErrorStateComponent {
  @Input() title = 'تعذر تحميل البيانات';
  @Input() description = 'حدثت مشكلة مؤقتة، يرجى المحاولة مرة أخرى.';
  @Input() actionText = 'إعادة المحاولة';
  @Output() action = new EventEmitter<void>();

  emitAction(): void {
    this.action.emit();
  }
}
