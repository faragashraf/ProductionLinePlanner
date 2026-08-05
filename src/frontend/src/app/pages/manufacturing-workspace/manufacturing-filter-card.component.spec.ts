import { HttpClientTestingModule } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { NoopAnimationsModule } from '@angular/platform-browser/animations';
import { buildFactoryStructureTree } from './factory-structure-tree.adapter';
import { FactoryStructureTreeSelectorComponent } from './factory-structure-tree-selector.component';
import { FactoryStructureTreeViewComponent } from './factory-structure-tree-view.component';
import { ManufacturingFilterCardComponent } from './manufacturing-filter-card.component';
import { ManufacturingWorkspaceModule } from './manufacturing-workspace.module';

describe('ManufacturingFilterCardComponent', () => {
  let fixture: ComponentFixture<ManufacturingFilterCardComponent>;
  let component: ManufacturingFilterCardComponent;

  beforeEach(() => {
    TestBed.configureTestingModule({ imports: [ManufacturingWorkspaceModule, HttpClientTestingModule, NoopAnimationsModule] });
    fixture = TestBed.createComponent(ManufacturingFilterCardComponent);
    component = fixture.componentInstance;
  });

  it('renders typed title, status, search, and disabled clear state', () => {
    component.title = 'فلاتر الموديلات';
    component.subtitle = 'اختر السياق ثم صفِّ النتائج';
    component.searchLabel = 'البحث بالاسم';
    component.searchPlaceholder = 'ابحث هنا';
    component.statusOptions = [{ label: 'الكل', value: 'all' }, { label: 'نشط', value: 'active' }];
    component.clearDisabled = true;
    fixture.detectChanges();

    expect(fixture.nativeElement.querySelector('h2').textContent).toContain('فلاتر الموديلات');
    expect(fixture.nativeElement.querySelector('.manufacturing-filter-card__heading p').textContent).toContain('اختر السياق');
    expect(fixture.nativeElement.querySelectorAll('.manufacturing-filter-card__group')).toHaveSize(2);
    expect(fixture.nativeElement.querySelector('.manufacturing-filter-card__search-control .pi-search')).not.toBeNull();
    expect(fixture.nativeElement.querySelector('input').placeholder).toBe('ابحث هنا');
    expect(fixture.nativeElement.querySelectorAll('select option')).toHaveSize(2);
    expect(fixture.nativeElement.querySelector('.manufacturing-filter-card__clear').disabled).toBeTrue();
  });

  it('emits search, status, and clear changes without owning screen logic', () => {
    const searchSpy = spyOn(component.searchValueChange, 'emit');
    const statusSpy = spyOn(component.statusValueChange, 'emit');
    const clearSpy = spyOn(component.clearFilters, 'emit');
    component.statusOptions = [{ label: 'الكل', value: 'all' }, { label: 'نشط', value: 'active' }];
    component.clearDisabled = false;
    fixture.detectChanges();

    fixture.nativeElement.querySelector('input').value = 'MOD';
    fixture.nativeElement.querySelector('input').dispatchEvent(new Event('input'));
    fixture.nativeElement.querySelector('select').value = 'active';
    fixture.nativeElement.querySelector('select').dispatchEvent(new Event('change'));
    fixture.nativeElement.querySelector('.manufacturing-filter-card__clear').click();

    expect(searchSpy).toHaveBeenCalledWith('MOD');
    expect(statusSpy).toHaveBeenCalledWith('active');
    expect(clearSpy).toHaveBeenCalled();
  });

  it('uses responsive grid contracts without horizontal overflow', () => {
    fixture.detectChanges();
    const card = fixture.nativeElement.querySelector('.manufacturing-filter-card') as HTMLElement;
    const fields = fixture.nativeElement.querySelector('.manufacturing-filter-card__fields') as HTMLElement;
    expect(getComputedStyle(fields).display).toBe('grid');
    expect(card.scrollWidth).toBeLessThanOrEqual(card.clientWidth);
  });
});

describe('FactoryStructureTreeSelectorComponent', () => {
  it('shows the full factory path and searches by code while preserving ancestors', () => {
    TestBed.configureTestingModule({ imports: [ManufacturingWorkspaceModule, HttpClientTestingModule, NoopAnimationsModule] });
    const fixture = TestBed.createComponent(FactoryStructureTreeSelectorComponent);
    const component = fixture.componentInstance;
    component.nodes = buildFactoryStructureTree({
      factories: [{ id: 'factory-1', code: 'F1', name: 'مصنع 1', isActive: true }],
      departments: [{ id: 'department-1', factoryId: 'factory-1', code: 'CUT', nameAr: 'القص', isActive: true }],
      lines: [{ id: 'line-1', factoryId: 'factory-1', departmentId: 'department-1', lineCode: 'L-01', name: 'خط القص', sequenceOrder: 1, isActive: true }],
      eligibility: new Map()
    });
    component.selectedNode = component.nodes[0].children![0].children![0];
    component.treeSearch = 'L-01';

    expect(component.selectedPath).toBe('مصنع 1 ← القص ← خط القص');
    expect(component.visibleNodes[0].children![0].children![0].data?.entityId).toBe('line-1');
  });

  it('emits a null selection when the scope is cleared', () => {
    TestBed.configureTestingModule({ imports: [ManufacturingWorkspaceModule, HttpClientTestingModule, NoopAnimationsModule] });
    const component = TestBed.createComponent(FactoryStructureTreeSelectorComponent).componentInstance;
    const selectionSpy = spyOn(component.selectedNodeChange, 'emit');
    component.clearSelection();
    expect(selectionSpy).toHaveBeenCalledWith(null);
  });

  it('emits only production-line selections in the default filter mode', () => {
    TestBed.configureTestingModule({ imports: [ManufacturingWorkspaceModule, HttpClientTestingModule, NoopAnimationsModule] });
    const component = TestBed.createComponent(FactoryStructureTreeSelectorComponent).componentInstance;
    component.nodes = structureNodes();
    const factory = component.nodes[0];
    const department = factory.children![0];
    const line = department.children![0];
    const selectionSpy = spyOn(component.selectedNodeChange, 'emit');
    const nodeSpy = spyOn(component.nodeSelect, 'emit');

    component.selectNode(factory);
    component.selectNode(department);
    expect(selectionSpy).not.toHaveBeenCalled();
    expect(nodeSpy).not.toHaveBeenCalled();

    component.selectNode(line);
    expect(selectionSpy).toHaveBeenCalledOnceWith(line);
    expect(nodeSpy).toHaveBeenCalledOnceWith(line);
  });

  it('uses a non-overlapping icon, text, and chevron trigger contract', () => {
    TestBed.configureTestingModule({ imports: [ManufacturingWorkspaceModule, HttpClientTestingModule, NoopAnimationsModule] });
    const fixture = TestBed.createComponent(FactoryStructureTreeSelectorComponent);
    fixture.componentInstance.nodes = structureNodes();
    fixture.componentInstance.selectedNode = fixture.componentInstance.nodes[0].children![0].children![0];
    fixture.detectChanges();

    const trigger = fixture.nativeElement.querySelector('.structure-selector__trigger') as HTMLElement;
    expect(trigger.querySelectorAll('.structure-selector__icon')).toHaveSize(1);
    expect(trigger.querySelectorAll('.structure-selector__text')).toHaveSize(1);
    expect(trigger.querySelectorAll('.structure-selector__chevron')).toHaveSize(1);
    expect(getComputedStyle(trigger).minWidth).toBe('0px');
    expect(trigger.scrollWidth).toBeLessThanOrEqual(trigger.clientWidth);
  });
});

describe('FactoryStructureTreeViewComponent selectable levels', () => {
  beforeEach(() => TestBed.configureTestingModule({ imports: [ManufacturingWorkspaceModule, HttpClientTestingModule, NoopAnimationsModule] }));

  it('marks factory and department as navigation-only and the production line as selectable', () => {
    const fixture = TestBed.createComponent(FactoryStructureTreeViewComponent);
    fixture.componentInstance.nodes = structureNodes();
    fixture.componentInstance.selectableTypes = ['line'];
    fixture.detectChanges();

    const [factory] = fixture.componentInstance.nodes;
    const department = factory.children![0];
    const line = department.children![0];
    expect(factory.selectable).toBeFalse();
    expect(department.selectable).toBeFalse();
    expect(line.selectable).toBeTrue();
  });

  it('does not emit selection while expanding navigation nodes', () => {
    const fixture = TestBed.createComponent(FactoryStructureTreeViewComponent);
    const component = fixture.componentInstance;
    component.nodes = structureNodes();
    component.selectableTypes = ['line'];
    fixture.detectChanges();
    const selectionSpy = spyOn(component.nodeSelect, 'emit');
    const expandSpy = spyOn(component.nodeExpand, 'emit');
    const collapseSpy = spyOn(component.nodeCollapse, 'emit');
    const factory = component.nodes[0];

    component.onNodeExpand({ node: factory });
    component.onNodeCollapse({ node: factory });

    expect(expandSpy).toHaveBeenCalledOnceWith(factory);
    expect(collapseSpy).toHaveBeenCalledOnceWith(factory);
    expect(selectionSpy).not.toHaveBeenCalled();
  });

  it('preserves an existing selection when a navigation-only node is clicked', () => {
    const fixture = TestBed.createComponent(FactoryStructureTreeViewComponent);
    const component = fixture.componentInstance;
    component.nodes = structureNodes();
    component.selectableTypes = ['line'];
    const line = component.nodes[0].children![0].children![0];
    component.selectedNode = line;
    const selectionChangeSpy = spyOn(component.selectedNodeChange, 'emit');

    component.onNodeSelect({ node: component.nodes[0] });

    expect(component.selectedNode).toBe(line);
    expect(selectionChangeSpy).not.toHaveBeenCalled();
  });
});

function structureNodes() {
  return buildFactoryStructureTree({
    factories: [{ id: 'factory-1', code: 'F1', name: 'مصنع 1', isActive: true }],
    departments: [{ id: 'department-1', factoryId: 'factory-1', code: 'CUT', nameAr: 'القص', isActive: true }],
    lines: [{ id: 'line-1', factoryId: 'factory-1', departmentId: 'department-1', lineCode: 'L-01', name: 'خط القص', sequenceOrder: 1, isActive: true }],
    eligibility: new Map()
  });
}
