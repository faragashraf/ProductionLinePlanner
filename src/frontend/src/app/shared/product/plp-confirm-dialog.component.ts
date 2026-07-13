import { ChangeDetectionStrategy, Component, Input } from '@angular/core';
import { ConfirmDialogModule } from 'primeng/confirmdialog';

/** One app-level ConfirmDialog host. Requests are issued through PlpConfirmationService. */
@Component({
  selector: 'plp-confirm-dialog',
  standalone: true,
  imports: [ConfirmDialogModule],
  template: `
    <p-confirmDialog
      [key]="key"
      [dismissableMask]="dismissableMask"
      [closable]="closable"
      styleClass="plp-product-confirm-dialog"
      appendTo="body"
    ></p-confirmDialog>
  `,
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class PlpConfirmDialogComponent {
  @Input() key = 'plp-confirm';
  @Input() dismissableMask = false;
  @Input() closable = true;
}
