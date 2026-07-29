import { ComponentFixture, TestBed } from '@angular/core/testing';
import { BehaviorSubject, of } from 'rxjs';
import { PERMISSIONS } from '../../core/config/permission-identifiers';
import { ManufacturingMasterDataApiService } from '../../core/services/manufacturing-master-data-api.service';
import { ManufacturingRealtimeService } from '../../core/services/manufacturing-realtime.service';
import { PermissionService } from '../../core/services/permission.service';
import { FactoryStructureFoundationPageComponent } from './factory-structure-foundation-page.component';
import { buildFactoryStructureContextMenu } from './factory-structure-tree-menu.builder';
import { buildFactoryStructureTree, filterFactoryStructureTree } from './factory-structure-tree.adapter';

describe('FactoryStructureFoundationPageComponent', () => {
  const factory = { id: 'fac-1', name: 'مصنع رئيسي', code: 'FAC-1', location: 'القاهرة', isActive: true };
  const department = { id: 'dep-1', factoryId: factory.id, code: 'CUT', nameAr: 'القص', sequenceOrder: 1, isActive: true };
  const line = { id: 'line-1', factoryId: factory.id, departmentId: department.id, name: 'خط القص', lineCode: 'L-1', sequenceOrder: 1, isActive: true };
  let api: jasmine.SpyObj<ManufacturingMasterDataApiService>;

  function configure(grants: string[] = [PERMISSIONS.factoryStructure.manage, PERMISSIONS.departments.manage, PERMISSIONS.stages.manage]): ComponentFixture<FactoryStructureFoundationPageComponent> {
    api = jasmine.createSpyObj<ManufacturingMasterDataApiService>('ManufacturingMasterDataApiService', [
      'factories', 'departments', 'allProductionLines', 'factoryStructureDeleteEligibility', 'createFactory', 'updateFactory', 'deleteFactory',
      'createDepartment', 'updateDepartment', 'deleteDepartment', 'createProductionLine', 'updateProductionLine', 'deleteProductionLine',
      'createMain', 'updateMain', 'deactivateMain', 'setMainActivation', 'createSub', 'updateSub', 'deactivateSub', 'setSubActivation'
    ]);
    api.factories.and.returnValue(of([factory]));
    api.departments.and.returnValue(of([department]));
    api.allProductionLines.and.returnValue(of([line]));
    api.factoryStructureDeleteEligibility.and.returnValue(of({ factories: [{ entityId: factory.id, canDelete: false }], departments: [{ entityId: department.id, canDelete: false }], lines: [{ entityId: line.id, canDelete: true }] }));
    ['createFactory', 'updateFactory', 'deleteFactory', 'createDepartment', 'updateDepartment', 'deleteDepartment', 'createProductionLine', 'updateProductionLine', 'deleteProductionLine', 'createMain', 'updateMain', 'deactivateMain', 'setMainActivation', 'createSub', 'updateSub', 'deactivateSub', 'setSubActivation'].forEach(method => (api[method as keyof typeof api] as jasmine.Spy).and.returnValue(of({})));
    const realtime = jasmine.createSpyObj<ManufacturingRealtimeService>('ManufacturingRealtimeService', ['watchScreen', 'registerLocalOperation']);
    realtime.watchScreen.and.returnValue(() => undefined);
    realtime.registerLocalOperation.and.returnValue('local-correlation');
    TestBed.configureTestingModule({ declarations: [FactoryStructureFoundationPageComponent], providers: [
      { provide: ManufacturingMasterDataApiService, useValue: api },
      { provide: ManufacturingRealtimeService, useValue: realtime },
      { provide: PermissionService, useValue: { permissions$: of(grants), hydrationState$: new BehaviorSubject('ready'), hasPermission: (permission: string) => grants.includes(permission) } }
    ] }).overrideComponent(FactoryStructureFoundationPageComponent, { set: { template: '' } });
    const fixture = TestBed.createComponent(FactoryStructureFoundationPageComponent);
    fixture.detectChanges();
    return fixture;
  }

  afterEach(() => TestBed.resetTestingModule());

  it('maps the real Factory → Department → Line hierarchy with stable entity metadata', () => {
    const tree = buildFactoryStructureTree({ factories: [factory], departments: [department], lines: [line], eligibility: new Map([[factory.id, { canDelete: false }], [department.id, { canDelete: false }], [line.id, { canDelete: true }]]) });
    const departmentNode = tree[0].children![0];
    const lineNode = departmentNode.children![0];
    expect(tree[0].data).toEqual(jasmine.objectContaining({ entityId: factory.id, entityType: 'factory' }));
    expect(departmentNode.data).toEqual(jasmine.objectContaining({ entityId: department.id, entityType: 'department', parentId: factory.id }));
    expect(lineNode.data).toEqual(jasmine.objectContaining({ entityId: line.id, entityType: 'line', parentId: department.id }));
  });

  it('keeps a matching ancestor path expanded when searching by name or code', () => {
    const tree = buildFactoryStructureTree({ factories: [factory], departments: [department], lines: [line], eligibility: new Map() });
    const filtered = filterFactoryStructureTree(tree, 'L-1');
    expect(filtered[0].expanded).toBeTrue();
    expect(filtered[0].children![0].expanded).toBeTrue();
    expect(filtered[0].children![0].children![0].data?.entityId).toBe(line.id);
  });

  it('does not load stages when a production line expands', () => {
    const fixture = configure();
    const component = fixture.componentInstance;
    const lineNode = component.treeNodes[0].children![0].children![0] as any;
    component.onNodeExpand({ node: lineNode });
    expect((lineNode.children ?? []).length).toBe(0);
    expect('mainStagesForLine' in api).toBeFalse();
  });

  it('builds context actions by node type and does not expose mutations to view-only users', () => {
    const actions: string[] = [];
    const full = buildFactoryStructureContextMenu('line', true, true, { canManageStructure: true, canManageDepartments: true }, item => actions.push(item));
    expect(full.map(item => item.label)).toContain('حذف');
    expect(full.map(item => item.label)).not.toContain('إضافة مرحلة رئيسية');
    const readOnly = buildFactoryStructureContextMenu('line', true, true, { canManageStructure: false, canManageDepartments: false }, item => actions.push(item));
    expect(readOnly).toEqual([]);
  });

  it('opens a child form with the correct parent identifier from the selected node', () => {
    const fixture = configure();
    const component = fixture.componentInstance;
    const departmentNode = component.treeNodes[0].children![0] as any;
    (component as any).setContextNode(departmentNode);
    (component as any).runNodeAction('add-line');
    expect(component.activeForm).toBe('line');
    expect(component.lineDraft).toEqual(jasmine.objectContaining({ factoryId: factory.id, departmentId: department.id }));
  });

  it('defers realtime reload while a form has unsaved edits', () => {
    const fixture = configure();
    const component = fixture.componentInstance;
    component.openAddFactory();
    (component as any).onRealtimeRefresh();
    expect(component.realtimeRefreshPending).toBeTrue();
    expect(component.activeForm).toBe('factory');
  });

  it('keeps immutable codes out of factory, department, and line update payloads even if the draft is changed programmatically', () => {
    const fixture = configure();
    const component = fixture.componentInstance;

    component.factoryDraft = { id: factory.id, name: 'اسم محدث', code: 'MUTATED-FACTORY', location: '', isActive: true };
    component.saveFactory();
    expect(api.updateFactory).toHaveBeenCalledWith(factory.id, jasmine.objectContaining({ name: 'اسم محدث' }), 'local-correlation');
    expect((api.updateFactory.calls.mostRecent().args[1] as Record<string, unknown>)['code']).toBeUndefined();

    component.departmentDraft = { id: department.id!, factoryId: factory.id, code: 'MUTATED-DEPARTMENT', nameAr: 'قسم محدث', nameEn: '', sequenceOrder: 1, isActive: true };
    component.saveDepartment();
    expect((api.updateDepartment.calls.mostRecent().args[1] as Record<string, unknown>)['code']).toBeUndefined();

    component.lineDraft = { id: line.id, factoryId: factory.id, departmentId: department.id!, name: 'خط محدث', lineCode: 'MUTATED-LINE', sequenceOrder: 1, isActive: true };
    component.saveLine();
    expect((api.updateProductionLine.calls.mostRecent().args[1] as Record<string, unknown>)['lineCode']).toBeUndefined();
  });
});
