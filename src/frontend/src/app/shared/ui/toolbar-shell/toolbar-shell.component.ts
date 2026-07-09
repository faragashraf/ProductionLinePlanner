import { ChangeDetectionStrategy, Component, Input } from '@angular/core';

@Component({
  selector: 'plp-toolbar-shell',
  templateUrl: './toolbar-shell.component.html',
  styleUrls: ['./toolbar-shell.component.scss'],
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class ToolbarShellComponent {
  @Input() title = '';
  @Input() description = '';
}
