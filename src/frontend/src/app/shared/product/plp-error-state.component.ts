import { ChangeDetectionStrategy, Component, EventEmitter, Input, Output } from '@angular/core';
import { PlpStateShellComponent } from './plp-state-shell.component';

@Component({
  selector: 'plp-product-error-state',
  standalone: true,
  imports: [PlpStateShellComponent],
  template: `
    <plp-state-shell
      [title]="title"
      [description]="description"
      icon="pi-exclamation-circle"
      tone="error"
      role="alert"
      [actionLabel]="retryLabel"
      (action)="retry.emit()"
    ></plp-state-shell>
  `,
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class PlpProductErrorStateComponent {
  @Input() title = 'تعذر إكمال العملية';
  @Input() description = 'حدثت مشكلة مؤقتة. يرجى المحاولة مرة أخرى.';
  @Input() retryLabel = 'إعادة المحاولة';
  @Output() retry = new EventEmitter<void>();
}
