import { ComponentFixture, TestBed } from '@angular/core/testing';
import { FormsModule, ReactiveFormsModule } from '@angular/forms';
import { of } from 'rxjs';
import { ActivatedRoute } from '@angular/router';
import { ManufacturingMasterDataApiService } from '../../core/services/manufacturing-master-data-api.service';
import { ManufacturingMasterDataPageComponent } from './manufacturing-master-data-page.component';

describe('ManufacturingMasterDataPageComponent', () => {
  let component: ManufacturingMasterDataPageComponent;
  let api: jasmine.SpyObj<ManufacturingMasterDataApiService>;

  beforeEach(async () => {
    api = jasmine.createSpyObj('ManufacturingMasterDataApiService', ['subStages', 'productionLines', 'mainStages', 'models', 'createSub', 'updateSub']);
    api.subStages.and.returnValue(of([])); api.productionLines.and.returnValue(of([])); api.mainStages.and.returnValue(of([])); api.models.and.returnValue(of([])); api.createSub.and.returnValue(of({ id: 'sub-1', mainStageId: 'main-1', name: 'Cutting', code: 'CUT', capacity: 10, sequenceOrder: 3, isActive: true })); api.updateSub.and.returnValue(of({ id: 'sub-1', mainStageId: 'main-1', name: 'Cutting', code: 'CUT', capacity: 10, sequenceOrder: 3, isActive: true }));
    await TestBed.configureTestingModule({ declarations: [ManufacturingMasterDataPageComponent], imports: [FormsModule, ReactiveFormsModule], providers: [{ provide: ManufacturingMasterDataApiService, useValue: api }, { provide: ActivatedRoute, useValue: { snapshot: { routeConfig: { path: 'stages' } } } }] }).overrideComponent(ManufacturingMasterDataPageComponent, { set: { template: '' } }).compileComponents();
    const fixture: ComponentFixture<ManufacturingMasterDataPageComponent> = TestBed.createComponent(ManufacturingMasterDataPageComponent);
    component = fixture.componentInstance;
  });

  it('maps the display sequence order to the DefaultOrder API contract', () => {
    component.subForm.setValue({ mainStageId: 'main-1', code: 'CUT', name: 'Cutting', capacity: 10, sequenceOrder: 3 });
    component.saveSub();
    expect(api.createSub).toHaveBeenCalledWith({ mainStageId: 'main-1', code: 'CUT', name: 'Cutting', capacity: 10, defaultOrder: 3 });
  });
});
