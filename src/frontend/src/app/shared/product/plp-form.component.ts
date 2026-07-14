import { ChangeDetectionStrategy, Component, EventEmitter, Input, Output } from '@angular/core';

@Component({
  selector: 'plp-form',
  standalone: true,
  template: `
    <form
      class="plp-product-form plp-form-grid"
      [class.plp-form-grid--two-columns]="columns === 2"
      [class.plp-density-compact]="density === 'compact'"
      [attr.aria-busy]="busy || null"
      novalidate
      (submit)="onSubmit($event)"
    >
      <ng-content></ng-content>
    </form>
  `,
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class PlpFormComponent {
  @Input() columns: 1 | 2 = 1;
  @Input() density: 'standard' | 'compact' = 'standard';
  @Input() busy = false;
  @Output() submitted = new EventEmitter<void>();

  onSubmit(event: SubmitEvent): void {
    event.preventDefault();
    this.submitted.emit();
  }
}
