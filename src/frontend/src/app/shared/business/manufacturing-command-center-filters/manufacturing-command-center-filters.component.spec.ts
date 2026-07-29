import { CommandCenterFilters } from '../../models/manufacturing-command-center.model';
import { ManufacturingCommandCenterFiltersComponent } from './manufacturing-command-center-filters.component';

describe('ManufacturingCommandCenterFiltersComponent', () => {
  it('emits one immutable, hierarchical scope for factory, department, and line choices', () => {
    const component = new ManufacturingCommandCenterFiltersComponent();
    const emitted: CommandCenterFilters[] = [];
    component.filtersChange.subscribe(filters => emitted.push(filters));

    component.catalog = {
      factories: [{ id: 'factory-1', name: 'مصنع', code: 'F' }],
      departments: [{ id: 'department-1', factoryId: 'factory-1', name: 'قسم', code: 'D' }],
      lines: [{ id: 'line-1', factoryId: 'factory-1', departmentId: 'department-1', name: 'خط', code: 'L' }]
    };
    component.onDepartmentChange('department-1');
    component.onLineChange('line-1');
    component.onStatusChange('Approved');
    component.onDateChange('2026-07-22');

    expect(emitted[0]).toEqual(jasmine.objectContaining({ factoryId: 'factory-1', departmentId: 'department-1', productionLineId: null }));
    expect(emitted[1]).toEqual(jasmine.objectContaining({ factoryId: 'factory-1', departmentId: 'department-1', productionLineId: 'line-1' }));
    expect(emitted[2].operationStatus).toBe('Approved');
    expect(emitted[3].operationDate).toBe('2026-07-22');
  });
});
