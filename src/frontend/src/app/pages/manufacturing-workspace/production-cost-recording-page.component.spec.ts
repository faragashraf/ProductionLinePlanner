import { ComponentFixture, TestBed } from '@angular/core/testing';
import { FormsModule, ReactiveFormsModule } from '@angular/forms';
import { of, throwError, NEVER } from 'rxjs';
import { PermissionService } from '../../core/services/permission.service';
import { ProductionCostRecordingApiService, StageProductionRecord } from '../../core/services/production-cost-recording-api.service';
import { ProductionCostRecordingPageComponent } from './production-cost-recording-page.component';

describe('ProductionCostRecordingPageComponent', () => {
  let component: ProductionCostRecordingPageComponent;
  let api: jasmine.SpyObj<ProductionCostRecordingApiService>;
  let permissions: { values: string[]; hasPermission: (permission: string) => boolean };
  const draft = (status: StageProductionRecord['status'] = 'Draft'): StageProductionRecord => ({ id: 'record-1', productionOrderId: 'order-1', productModelStageId: 'stage-1', productionDate: '2026-07-13', producedQuantity: 10, acceptedQuantity: 10, rejectedQuantity: 0, status, stageCode: 'SEW', stageName: 'Sew', productModelCode: 'M-1', productModelName: 'Model', piecePrice: 1, compensationMode: 'SharedPercentage', totalWorkerEarnings: 10, concurrencyToken: 'token-1', workers: [] });

  beforeEach(async () => {
    api = jasmine.createSpyObj('ProductionCostRecordingApiService', ['listOrders', 'listRecords', 'dailyReport', 'listModels', 'listWorkers', 'approve', 'cancel', 'getRecord']);
    api.listOrders.and.returnValue(of([])); api.listRecords.and.returnValue(of([])); api.dailyReport.and.returnValue(of([])); api.listModels.and.returnValue(of([])); api.listWorkers.and.returnValue(of([]));
    permissions = { values: ['production.view', 'production.approve'], hasPermission(permission: string) { return this.values.includes(permission); } };
    await TestBed.configureTestingModule({ declarations: [ProductionCostRecordingPageComponent], imports: [FormsModule, ReactiveFormsModule], providers: [{ provide: ProductionCostRecordingApiService, useValue: api }, { provide: PermissionService, useValue: permissions }] }).overrideComponent(ProductionCostRecordingPageComponent, { set: { template: '' } }).compileComponents();
    const fixture: ComponentFixture<ProductionCostRecordingPageComponent> = TestBed.createComponent(ProductionCostRecordingPageComponent); component = fixture.componentInstance;
  });

  it('filters draft, approved and cancelled records by order, date and status', () => {
    component.records = [draft('Draft'), { ...draft('Approved'), id: 'record-2', productionDate: '2026-07-14' }, { ...draft('Cancelled'), id: 'record-3' }];
    component.recordStatusFilter = 'Approved'; component.recordDateFilter = '2026-07-14'; component.recordOrderFilter = 'order-1';
    expect(component.filteredRecords().map(record => record.id)).toEqual(['record-2']);
  });

  it('approves only a draft when the user has production.approve and sends its concurrency token', () => {
    spyOn(window, 'confirm').and.returnValue(true); api.approve.and.returnValue(of(draft('Approved'))); component.records = [draft()];
    component.approve(draft());
    expect(api.approve).toHaveBeenCalledWith('record-1', 'token-1');
    permissions.values = ['production.view']; component.approve(draft());
    expect(api.approve).toHaveBeenCalledTimes(1);
  });

  it('does not send duplicate approval requests while one is pending', () => {
    spyOn(window, 'confirm').and.returnValue(true); api.approve.and.returnValue(NEVER); component.approve(draft()); component.approve(draft());
    expect(api.approve).toHaveBeenCalledTimes(1);
  });

  it('shows the required conflict message for a 409 response', () => {
    spyOn(window, 'confirm').and.returnValue(true); api.approve.and.returnValue(throwError(() => ({ status: 409 }))); component.approve(draft());
    expect(component.error).toBe('تم تعديل السجل بواسطة مستخدم آخر. حدّث البيانات وحاول مرة أخرى.');
  });

  it('opens approved and cancelled records as read-only', () => {
    api.getRecord.and.returnValue(of(draft('Approved'))); component.openRecord(draft('Approved'));
    expect(component.recordForm.disabled).toBeTrue();
  });
});
