import { ChangeDetectionStrategy, Component, EventEmitter, Input, Output } from '@angular/core';
import { PermissionCatalogGroup } from '../../../core/services/iam-admin.service';

@Component({
  selector: 'plp-permission-selection',
  templateUrl: './permission-selection.component.html',
  styleUrls: ['./permission-selection.component.scss'],
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class PermissionSelectionComponent {
  @Input() groups: PermissionCatalogGroup[] = [];
  @Input() selected: string[] = [];
  @Input() disabled = false;
  @Output() selectedChange = new EventEmitter<string[]>();

  isSelected(permission: string): boolean {
    return this.selected.some((item) => item.toLowerCase() === permission.toLowerCase());
  }

  toggle(permission: string, checked: boolean): void {
    const withoutPermission = this.selected.filter((item) => item.toLowerCase() !== permission.toLowerCase());
    this.selectedChange.emit(checked ? [...withoutPermission, permission].sort() : withoutPermission);
  }
}
