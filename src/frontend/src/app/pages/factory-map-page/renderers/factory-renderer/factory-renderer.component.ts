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

  getWorkersSummary(): string {
    return this.layout.workerRequirementDefined === false
      ? `${this.layout.workersCurrent ?? 0} مسكن - الاحتياج غير محدد`
      : `${this.layout.workersCurrent ?? 0} / ${this.layout.workersRequired ?? 0}`;
  }

  onLineSelected(lineId: string): void {
    this.lineSelected.emit(lineId);
  }

  get lineGroups(): Array<{ key: string; name: string; lines: ProductionLineLayout[] }> {
    const groups = new Map<string, { key: string; name: string; lines: ProductionLineLayout[] }>();
    for (const line of this.layout.lines) {
      const key = line.departmentId || 'unassigned';
      const name = line.departmentName || 'غير مرتبط بقسم';
      const group = groups.get(key) ?? { key, name, lines: [] };
      group.lines.push(line);
      groups.set(key, group);
    }
    return [...groups.values()];
  }

  trackByGroup(_index: number, group: { key: string }): string {
    return group.key;
  }

  trackByLine(_index: number, line: ProductionLineLayout): string {
    return line.id;
  }
}
