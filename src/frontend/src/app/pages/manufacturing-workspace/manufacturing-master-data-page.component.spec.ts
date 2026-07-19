import { ComponentFixture, fakeAsync, TestBed, tick } from '@angular/core/testing';
import { FormsModule, ReactiveFormsModule } from '@angular/forms';
import { of, throwError } from 'rxjs';
import { ActivatedRoute } from '@angular/router';
import { ManufacturingMasterDataApiService } from '../../core/services/manufacturing-master-data-api.service';
import { ManufacturingMasterDataPageComponent } from './manufacturing-master-data-page.component';

describe('ManufacturingMasterDataPageComponent', () => {
  let component: ManufacturingMasterDataPageComponent;
  let api: jasmine.SpyObj<ManufacturingMasterDataApiService>;

  beforeEach(async () => {
    api = jasmine.createSpyObj('ManufacturingMasterDataApiService', ['subStages', 'searchSubStages', 'productionLines', 'mainStages', 'allMainStages', 'allSubStages', 'models', 'createSub', 'updateSub', 'setMainActivation']);
    api.subStages.and.returnValue(of([])); api.searchSubStages.and.returnValue(of({ items: [], totalCount: 0, pageNumber: 1, pageSize: 50 })); api.productionLines.and.returnValue(of([])); api.mainStages.and.returnValue(of([])); api.allMainStages.and.returnValue(of([])); api.allSubStages.and.returnValue(of([])); api.models.and.returnValue(of([])); api.createSub.and.returnValue(of({ id: 'sub-1', mainStageId: 'main-1', name: 'Cutting', code: 'CUT', capacity: 10, sequenceOrder: 3, isActive: true })); api.updateSub.and.returnValue(of({ id: 'sub-1', mainStageId: 'main-1', name: 'Cutting', code: 'CUT', capacity: 10, sequenceOrder: 3, isActive: true })); api.setMainActivation.and.returnValue(of({ id: 'main-1', productionLineId: 'line-1', name: 'Main', sequenceOrder: 1, isCritical: false, isActive: true }));
    await TestBed.configureTestingModule({ declarations: [ManufacturingMasterDataPageComponent], imports: [FormsModule, ReactiveFormsModule], providers: [{ provide: ManufacturingMasterDataApiService, useValue: api }, { provide: ActivatedRoute, useValue: { snapshot: { routeConfig: { path: 'stages' } } } }] }).overrideComponent(ManufacturingMasterDataPageComponent, { set: { template: '' } }).compileComponents();
    const fixture: ComponentFixture<ManufacturingMasterDataPageComponent> = TestBed.createComponent(ManufacturingMasterDataPageComponent);
    component = fixture.componentInstance;
  });

  it('maps the display sequence order to the DefaultOrder API contract', () => {
    component.subForm.setValue({ mainStageId: 'main-1', code: 'CUT', name: 'Cutting', capacity: 10, sequenceOrder: 3 });
    component.saveSub();
    expect(api.createSub).toHaveBeenCalledWith({ mainStageId: 'main-1', code: 'CUT', name: 'Cutting', capacity: 10, defaultOrder: 3 });
  });

  it('loads active and inactive catalog records once for stage administration only', () => {
    component.ngOnInit();

    expect(api.productionLines).toHaveBeenCalledTimes(1);
    expect(api.allMainStages).toHaveBeenCalledTimes(1);
    expect(api.allSubStages).toHaveBeenCalledTimes(1);
    expect(api.mainStages).not.toHaveBeenCalled();
    expect(api.subStages).not.toHaveBeenCalled();
  });

  it('filters already-loaded stages locally and exposes explicit status and missing-order labels', () => {
    component.mains = [{ id: 'active', productionLineId: 'line-1', name: 'نشطة', sequenceOrder: 1, isCritical: false, isActive: true }, { id: 'inactive', productionLineId: 'line-1', name: 'معطلة', sequenceOrder: 2, isCritical: false, isActive: false }];
    component.setStageStatusFilter('inactive');
    expect(component.visibleMains.map(item => item.id)).toEqual(['inactive']);
    expect(component.stageStatusLabel(false)).toBe('معطلة');
    expect(component.formatOrder(null)).toBe('غير محدد');
    expect(component.formatOrder(0)).toBe('0');
    expect(component.formatOrder(0, 1)).toBe('غير محدد');
  });

  it('reactivates an inactive main stage through the existing PATCH contract', () => {
    spyOn(window, 'confirm').and.returnValue(true);
    component.mains = [{ id: 'main-1', productionLineId: 'line-1', name: 'Main', sequenceOrder: 1, isCritical: false, isActive: false }];

    component.setMainActive(component.mains[0]);

    expect(api.setMainActivation).toHaveBeenCalledWith('main-1', true);
    expect(component.mains[0].isActive).toBeTrue();
  });

  it('clears a failed save error after a successful retry while retaining the locally upserted record', () => {
    component.subFormVisible = true;
    component.subForm.setValue({ mainStageId: 'main-1', code: 'CUT', name: 'Cutting', capacity: 10, sequenceOrder: 3 });
    api.createSub.and.returnValue(throwError(() => new Error('تعذر حفظ المرحلة.')));

    component.saveSub();

    expect(component.error).toBe('تعذر حفظ المرحلة.');
    expect(component.subFormVisible).toBeTrue();
    expect(component.subForm.getRawValue().name).toBe('Cutting');

    api.createSub.and.returnValue(of({ id: 'sub-1', mainStageId: 'main-1', name: 'Cutting', code: 'CUT', capacity: 10, sequenceOrder: 3, isActive: true }));
    component.saveSub();

    expect(component.error).toBe('');
    expect(component.subs.map(item => item.id)).toEqual(['sub-1']);
  });

  it('keeps the debounced stage search alive after one request fails', fakeAsync(() => {
    component.ngOnInit();
    api.searchSubStages.and.returnValue(throwError(() => new Error('تعذر البحث.')));
    component.onModelStageSearch('فشل');
    tick(250);
    expect(component.error).toBe('تعذر البحث.');

    api.searchSubStages.and.returnValue(of({ items: [{ id: 'sub-2', mainStageId: 'main-1', name: 'مرحلة', code: 'S2', capacity: 1, sequenceOrder: 1, isActive: true }], totalCount: 1, pageNumber: 1, pageSize: 50 }));
    component.onModelStageSearch('مرحلة');
    tick(250);

    expect(component.modelStageOptions.map(item => item.id)).toEqual(['sub-2']);
    expect(component.error).toBe('');
  }));

  it('stops pending stage searches when the component is destroyed', fakeAsync(() => {
    component.ngOnInit();
    api.searchSubStages.calls.reset();
    component.onModelStageSearch('لن ينفذ');
    component.ngOnDestroy();
    tick(250);
    expect(api.searchSubStages).not.toHaveBeenCalled();
  }));
});
