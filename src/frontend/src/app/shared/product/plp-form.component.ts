import { ChangeDetectionStrategy, Component, EventEmitter, Input, Output } from '@angular/core';
import { FormGroup, ReactiveFormsModule } from '@angular/forms';

@Component({
  selector: 'plp-form',
  standalone: true,
  imports: [ReactiveFormsModule],
  template: `
    <form
      class="plp-product-form plp-form-grid"
      [class.plp-form-grid--two-columns]="columns === 2"
      [class.plp-density-compact]="density === 'compact'"
      [attr.aria-busy]="busy || null"
      [formGroup]="formGroup"
      novalidate
      (submit)="onSubmit($event)"
    >
      <ng-content></ng-content>
    </form>
  `,
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class PlpFormComponent {
  // A neutral group preserves the component's existing content-only use while
  // allowing operational pages to opt into one shared reactive submit shell.
  @Input() formGroup: FormGroup<any> = new FormGroup({});
  @Input() columns: 1 | 2 = 1;
  @Input() density: 'standard' | 'compact' = 'standard';
  @Input() busy = false;
  @Output() submitted = new EventEmitter<void>();

  onSubmit(event: SubmitEvent): void {
    event.preventDefault();
    this.submitted.emit();
  }
}
