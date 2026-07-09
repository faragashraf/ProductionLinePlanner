import { ChangeDetectionStrategy, Component, EventEmitter, Input, Output } from '@angular/core';
import { FactoryLayout, ProductionLineLayout } from '../../../../shared/models/factory-visualization.model';

@Component({
  selector: 'plp-factory-renderer',
  templateUrl: './factory-renderer.component.html',
  styleUrls: ['./factory-renderer.component.scss'],
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class FactoryRendererComponent {
  @Input() layout!: FactoryLayout;
  @Output() lineSelected = new EventEmitter<string>();

  getLineStatusText(): string {
    const lines = this.layout.lines.length;
    if (lines === 0) {
      return 'لا توجد خطوط معلنة في الميتاداتا.';
    }
    return `مصمّم عبر ${lines} خط`;
  }

  onLineSelected(lineId: string): void {
    this.lineSelected.emit(lineId);
  }

  trackByLine(_index: number, line: ProductionLineLayout): string {
    return line.id;
  }
}
