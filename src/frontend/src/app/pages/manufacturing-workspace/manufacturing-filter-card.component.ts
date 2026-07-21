import { Component, EventEmitter, Input, Output } from '@angular/core';

export interface ManufacturingFilterOption { label: string; value: string; }

@Component({
  selector: 'app-manufacturing-filter-card',
  templateUrl: './manufacturing-filter-card.component.html',
  styleUrls: ['./manufacturing-filter-card.component.scss']
})
export class ManufacturingFilterCardComponent {
  @Input() title = 'الفلاتر';
  @Input() searchLabel = 'البحث';
  @Input() searchPlaceholder = 'ابحث';
  @Input() searchValue = '';
  @Input() showSearch = true;
  @Input() statusLabel = 'الحالة';
  @Input() statusValue = 'all';
  @Input() statusOptions: readonly ManufacturingFilterOption[] = [];
  @Input() clearDisabled = true;
  @Input() clearLabel = 'مسح الفلاتر';

  @Output() searchValueChange = new EventEmitter<string>();
  @Output() statusValueChange = new EventEmitter<string>();
  @Output() clearFilters = new EventEmitter<void>();
}
