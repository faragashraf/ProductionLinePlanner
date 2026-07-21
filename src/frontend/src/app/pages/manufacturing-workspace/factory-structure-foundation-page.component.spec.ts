import { ComponentFixture, TestBed } from '@angular/core/testing';
import { FormsModule } from '@angular/forms';
import { By } from '@angular/platform-browser';
import { NoopAnimationsModule } from '@angular/platform-browser/animations';
import { ButtonModule } from 'primeng/button';
import { TableModule } from 'primeng/table';
import { BehaviorSubject, of } from 'rxjs';
import { PERMISSIONS } from '../../core/config/permission-identifiers';
import { ManufacturingMasterDataApiService } from '../../core/services/manufacturing-master-data-api.service';
import { ManufacturingRealtimeService } from '../../core/services/manufacturing-realtime.service';
import { PermissionService } from '../../core/services/permission.service';
import { PlpExpandableFormComponent } from '../../shared/product/plp-expandable-form.component';
import { PlpResponsiveTableDirective } from '../../shared/product/plp-responsive-table.directive';
import { PlpTablePaginationDirective } from '../../shared/product/plp-table-pagination.directive';
import { PlpProductToolbarComponent } from '../../shared/product/plp-toolbar.component';
import { SharedModule } from '../../shared/shared.module';
import { FactoryStructureFoundationPageComponent } from './factory-structure-foundation-page.component';

describe('FactoryStructureFoundationPageComponent', () => {
  function configure(manage = true): ComponentFixture<FactoryStructureFoundationPageComponent> {
    const masterData = jasmine.createSpyObj<ManufacturingMasterDataApiService>('ManufacturingMasterDataApiService', [
      'factories', 'departments', 'allProductionLines', 'createFactory', 'updateFactory',
      'createDepartment', 'updateDepartment', 'deleteDepartment', 'createProductionLine', 'updateProductionLine'
    ]);
    masterData.factories.and.returnValue(of([
      { id: 'fac-1', name: 'مصنع رئيسي', code: 'FAC-1', location: 'القاهرة', isActive: true },
      { id: 'fac-2', name: 'مصنع آخر', code: 'FAC-2', location: 'الجيزة', isActive: true }
    ]));
    masterData.departments.and.returnValue(of([
      { id: 'dep-1', factoryId: 'fac-1', code: 'CUT', nameAr: 'القص', sequenceOrder: 1, isActive: true },
      { id: 'dep-2', factoryId: 'fac-1', code: 'SEW', nameAr: 'الخياطة', sequenceOrder: 2, isActive: true },
      { id: 'dep-3', factoryId: 'fac-2', code: 'PACK', nameAr: 'التعبئة', sequenceOrder: 1, isActive: true }
    ]));
    masterData.allProductionLines.and.returnValue(of([
      { id: 'line-1', factoryId: 'fac-1', departmentId: 'dep-1', departmentCode: 'CUT', departmentNameAr: 'القص', name: 'خط القص', lineCode: 'L-1', sequenceOrder: 1, isActive: true },
      { id: 'line-2', factoryId: 'fac-1', departmentId: 'dep-2', departmentCode: 'SEW', departmentNameAr: 'الخياطة', name: 'خط الخياطة', lineCode: 'L-2', sequenceOrder: 2, isActive: true },
      { id: 'line-legacy', factoryId: 'fac-1', departmentId: null, name: 'خط قديم', lineCode: 'L-0', sequenceOrder: 3, isActive: true }
    ]));
    masterData.createFactory.and.returnValue(of({ id: 'fac-new', name: 'مصنع جديد', code: 'NEW', isActive: true }));
    masterData.updateFactory.and.returnValue(of({ id: 'fac-1', name: 'مصنع رئيسي', code: 'FAC-1', isActive: true }));
    masterData.createDepartment.and.returnValue(of({ id: 'dep-new', factoryId: 'fac-1', code: 'NEW', nameAr: 'قسم جديد', isActive: true }));
    masterData.updateDepartment.and.returnValue(of({ id: 'dep-1', factoryId: 'fac-1', code: 'CUT', nameAr: 'القص', isActive: true }));
    masterData.deleteDepartment.and.returnValue(of({}));
    masterData.createProductionLine.and.returnValue(of({ id: 'line-new', factoryId: 'fac-1', departmentId: 'dep-1', name: 'خط جديد', sequenceOrder: 1, isActive: true }));
    masterData.updateProductionLine.and.returnValue(of({ id: 'line-1', factoryId: 'fac-1', departmentId: 'dep-1', name: 'خط القص', sequenceOrder: 1, isActive: true }));

    const hydration = new BehaviorSubject<'ready'>('ready');
    const grantedPermissions = new Set<string>(manage ? [PERMISSIONS.factoryStructure.manage, PERMISSIONS.departments.manage] : []);
    const realtime = jasmine.createSpyObj<ManufacturingRealtimeService>('ManufacturingRealtimeService', ['watchScreen', 'registerLocalOperation']);
    realtime.watchScreen.and.returnValue(() => undefined);
    realtime.registerLocalOperation.and.returnValue('local-correlation');
    TestBed.configureTestingModule({
      declarations: [FactoryStructureFoundationPageComponent],
      imports: [FormsModule, SharedModule, ButtonModule, TableModule, PlpResponsiveTableDirective, PlpTablePaginationDirective, PlpExpandableFormComponent, PlpProductToolbarComponent, NoopAnimationsModule],
      providers: [
        { provide: ManufacturingMasterDataApiService, useValue: masterData },
        { provide: ManufacturingRealtimeService, useValue: realtime },
        { provide: PermissionService, useValue: {
          permissions$: of([...grantedPermissions]),
          hydrationState$: hydration.asObservable(),
          get hydrationState() { return 'ready'; },
          hasPermission: (permission: string) => grantedPermissions.has(permission),
          hasAccess: (requirement: { permission?: string; requireAny?: string | string[]; requireAll?: string | string[] }) => {
            if (requirement.permission) return grantedPermissions.has(requirement.permission);
            const required = requirement.requireAll ?? requirement.requireAny;
            if (!required) return true;
            const permissions = Array.isArray(required) ? required : [required];
            return requirement.requireAll ? permissions.every(permission => grantedPermissions.has(permission)) : permissions.some(permission => grantedPermissions.has(permission));
          }
        } }
      ]
    });
    const fixture = TestBed.createComponent(FactoryStructureFoundationPageComponent);
    fixture.detectChanges();
    return fixture;
  }

  afterEach(() => TestBed.resetTestingModule());

  it('renders only Factory → Department → Line management and no stage or worker panels', () => {
    const fixture = configure();
    const text = fixture.nativeElement.textContent as string;

    expect(text).toContain('المصانع');
    expect(text).toContain('الأقسام المحلية');
    expect(text).toContain('خطوط الإنتاج');
    expect(text).not.toContain('المراحل الرئيسية');
    expect(text).not.toContain('المراحل التشغيلية');
    expect(text).not.toContain('العاملون المسكنون للمرحلة الفرعية');
    expect(fixture.debugElement.query(By.css('#factoryStructureAssignWorkerButton'))).toBeNull();
  });

  it('keeps departments and their lines operationally filterable under the selected factory', () => {
    const fixture = configure();
    const component = fixture.componentInstance;

    component.selectDepartment('dep-1');
    fixture.detectChanges();

    expect(component.visibleDepartments.map(department => department.id)).toEqual(['dep-1', 'dep-2']);
    expect(component.visibleLines.map(line => line.id)).toEqual(['line-1']);
    expect(fixture.nativeElement.textContent).toContain('خط القص');
    expect(fixture.nativeElement.textContent).not.toContain('خط الخياطة');
  });

  it('keeps unassigned legacy lines visible in their administrative group', () => {
    const fixture = configure();
    const component = fixture.componentInstance;

    component.selectDepartment('unassigned');
    fixture.detectChanges();

    expect(component.unassignedLines.map(line => line.id)).toEqual(['line-legacy']);
    expect(component.visibleLines.map(line => line.id)).toEqual(['line-legacy']);
    expect(fixture.nativeElement.textContent).toContain('خط قديم');
  });

  it('clears the selected line when the selected factory or department changes', () => {
    const fixture = configure();
    const component = fixture.componentInstance;

    component.selectLine('line-1');
    component.selectDepartment('dep-1');
    expect(component.selectedLineId).toBe('');

    component.selectLine('line-1');
    component.selectFactory('fac-2');
    expect(component.selectedDepartmentId).toBe('');
    expect(component.selectedLineId).toBe('');
  });

  it('requires a department when creating a new production line without changing legacy line support', () => {
    const fixture = configure();
    const component = fixture.componentInstance;
    const masterData = TestBed.inject(ManufacturingMasterDataApiService) as jasmine.SpyObj<ManufacturingMasterDataApiService>;

    component.lineDraft = { id: '', factoryId: 'fac-1', departmentId: '', name: 'خط جديد', lineCode: '', sequenceOrder: 1 };
    component.saveLine();

    expect(masterData.createProductionLine).not.toHaveBeenCalled();
    expect(component.errorMessage).toContain('القسم');
  });

  it('passes a local correlation when saving a factory-structure mutation', () => {
    const fixture = configure();
    const component = fixture.componentInstance;
    const masterData = TestBed.inject(ManufacturingMasterDataApiService) as jasmine.SpyObj<ManufacturingMasterDataApiService>;
    const realtime = TestBed.inject(ManufacturingRealtimeService) as jasmine.SpyObj<ManufacturingRealtimeService>;
    component.factoryDraft = { id: '', name: 'مصنع جديد', code: 'NEW', location: 'القاهرة' };

    component.saveFactory();

    expect(masterData.createFactory).toHaveBeenCalledWith({ name: 'مصنع جديد', code: 'NEW', location: 'القاهرة', isActive: true }, 'local-correlation');
    expect(realtime.registerLocalOperation).toHaveBeenCalledWith('factory-structure');
  });

  it('keeps hierarchy management forms permission-gated', () => {
    const fixture = configure(false);
    expect(fixture.debugElement.query(By.css('form'))).toBeNull();
  });
});
