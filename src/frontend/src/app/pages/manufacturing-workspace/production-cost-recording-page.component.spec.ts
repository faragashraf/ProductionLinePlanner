import { ComponentFixture, TestBed } from '@angular/core/testing';
import { FormsModule, ReactiveFormsModule } from '@angular/forms';
import { ActivatedRoute, ParamMap, convertToParamMap } from '@angular/router';
import { BehaviorSubject, NEVER, Subject, TimeoutError, of, throwError } from 'rxjs';
import { AssignmentsApiService, SubStageWorkerContext } from '../../core/services/assignments-api.service';
import { AttendanceApiService } from '../../core/services/attendance-api.service';
import { ManufacturingMasterDataApiService } from '../../core/services/manufacturing-master-data-api.service';
import { PermissionService } from '../../core/services/permission.service';
import { ProductProductionReadiness, ProductionCostRecordingApiService, StageProductionRecord } from '../../core/services/production-cost-recording-api.service';
import { ProductionCostRecordingPageComponent } from './production-cost-recording-page.component';

describe('ProductionCostRecordingPageComponent', () => {
  let component: ProductionCostRecordingPageComponent;
  let api: jasmine.SpyObj<ProductionCostRecordingApiService>;
  let masterData: jasmine.SpyObj<ManufacturingMasterDataApiService>;
  let assignments: jasmine.SpyObj<AssignmentsApiService>;
  let attendance: jasmine.SpyObj<AttendanceApiService>;
  let permissions: { values: string[]; hasPermission: (permission: string) => boolean };
  let fixture: ComponentFixture<ProductionCostRecordingPageComponent>;
  let routeQueryParams: BehaviorSubject<ParamMap>;
  const draft = (status: StageProductionRecord['status'] = 'Draft'): StageProductionRecord => ({
    id: 'record-1', productionOrderId: 'order-1', productModelStageId: 'stage-1', productionDate: '2026-07-13', producedQuantity: 10, acceptedQuantity: 10, rejectedQuantity: 0,
    status, stageCode: 'SEW', stageName: 'Sew', productModelCode: 'M-1', productModelName: 'Model', factoryCode: 'F-1', factoryName: 'Factory', productionLineCode: 'L-1', productionLineName: 'Line', mainStageName: 'Main', piecePrice: 1,
    compensationMode: 'SharedPercentage', totalWorkerEarnings: 10, concurrencyToken: 'token-1', workers: []
  });
  const financiallyConsistentDraft = (): StageProductionRecord => ({
    ...draft(),
    workers: [{ workerId: 'worker-1', workerCode: 'W1', workerName: 'Worker', equivalentQuantity: 10, calculatedEarning: 10 }]
  });
  const syncResult = {
    syncDateUtc: '2026-07-15T00:00:00Z', sourceUsersCount: 4, sourceCheckInsCount: 3,
    matchedWorkersCount: 2, unmatchedSourceUsersCount: 1, workersWithoutAttendanceCount: 1,
    insertedRecords: 2, updatedRecords: 0, skippedRecords: 0
  };
  const emptyWorkerContext = (subStageId: string, workerId?: string): SubStageWorkerContext => ({
    subStageId,
    currentWorkers: workerId
      ? [{ workerId, employeeCode: workerId, fullName: workerId, attendanceStatus: 'Present', attendanceTimeUtc: null, assignmentType: 'Default', effectiveSubStageId: subStageId, isAvailable: true }]
      : [],
    presentWorkers: [],
    availableWorkers: [],
    unavailableWorkersCount: 0
  });
  const readiness = (productionDate = '2026-07-13'): ProductProductionReadiness => ({
    productModelId: 'model-1', productModelCode: 'M-1', productModelName: 'Model', productionLineId: 'line-1', productionDate,
    totalStages: 1, readyStages: 1, stagesWithoutWorkers: 0, stagesNeedingCompensationReview: 0, stagesWithoutAttendanceData: 0,
    incompleteStages: 0, overallReadinessState: 'Ready', readyForWorkflowTest: true, readyForProductionEntry: true,
    readyForFinancialApproval: true, stages: [], problemStages: []
  });

  beforeEach(async () => {
    routeQueryParams = new BehaviorSubject<ParamMap>(convertToParamMap({}));
    api = jasmine.createSpyObj('ProductionCostRecordingApiService', ['listOrders', 'listRecords', 'dailyReport', 'listModels', 'listModelStages', 'getProductReadiness', 'approve', 'cancelProductionApproval', 'getRecord', 'calculatePreview', 'createDraft', 'updateDraft']);
    api.listOrders.and.returnValue(of([])); api.listRecords.and.returnValue(of([])); api.dailyReport.and.returnValue(of([])); api.listModels.and.returnValue(of([])); api.listModelStages.and.returnValue(of([])); api.getProductReadiness.and.returnValue(of(readiness()));
    api.calculatePreview.and.returnValue(of(draft())); api.createDraft.and.returnValue(of(draft())); api.updateDraft.and.returnValue(of(draft())); api.cancelProductionApproval.and.returnValue(of(draft('Cancelled')));
    masterData = jasmine.createSpyObj('ManufacturingMasterDataApiService', ['factories', 'productionLines', 'allProductionLines', 'mainStagesForLine', 'allMainStages', 'subStagesForMainStage', 'allSubStages']);
    masterData.factories.and.returnValue(of([])); masterData.productionLines.and.returnValue(of([])); masterData.allProductionLines.and.returnValue(of([])); masterData.mainStagesForLine.and.returnValue(of([])); masterData.allMainStages.and.returnValue(of([])); masterData.subStagesForMainStage.and.returnValue(of([])); masterData.allSubStages.and.returnValue(of([]));
    assignments = jasmine.createSpyObj('AssignmentsApiService', ['getSubStageWorkerContext', 'createDefaultAssignment', 'removeDefaultAssignment']);
    assignments.getSubStageWorkerContext.and.returnValue(of({ subStageId: 'sub-1', currentWorkers: [], presentWorkers: [], availableWorkers: [], unavailableWorkersCount: 0 }));
    assignments.createDefaultAssignment.and.returnValue(of({ assignmentId: 'a-1', workerId: 'worker-1', assignmentType: 'Default', subStageId: 'sub-1', fromSubStageId: null, toSubStageId: null, startsAtUtc: null, endsAtUtc: null, status: 'Active', replacementForWorkerId: null }));
    assignments.removeDefaultAssignment.and.returnValue(of({ assignmentId: 'a-1', workerId: 'worker-1', assignmentType: 'Default', subStageId: 'sub-1', fromSubStageId: null, toSubStageId: null, startsAtUtc: null, endsAtUtc: null, status: 'Cancelled', replacementForWorkerId: null }));
    attendance = jasmine.createSpyObj('AttendanceApiService', ['getToday', 'syncToday', 'getForProductionDate', 'syncForProductionDate']);
    attendance.getToday.and.returnValue(of({ date: '2026-07-15T00:00:00Z', items: [] }));
    attendance.syncToday.and.returnValue(of(syncResult));
    attendance.getForProductionDate.and.returnValue(of({ date: '2026-07-15T00:00:00Z', items: [] }));
    attendance.syncForProductionDate.and.returnValue(of(syncResult));
    permissions = { values: ['production.view', 'production.approve', 'production.record', 'assignments.view', 'assignments.manage', 'attendance.view', 'attendance.sync'], hasPermission(permission: string) { return this.values.includes(permission); } };
    await TestBed.configureTestingModule({
      declarations: [ProductionCostRecordingPageComponent],
      imports: [FormsModule, ReactiveFormsModule],
      providers: [
        { provide: ProductionCostRecordingApiService, useValue: api },
        { provide: ManufacturingMasterDataApiService, useValue: masterData },
        { provide: AssignmentsApiService, useValue: assignments },
        { provide: AttendanceApiService, useValue: attendance },
        { provide: PermissionService, useValue: permissions },
        { provide: ActivatedRoute, useValue: { queryParamMap: routeQueryParams.asObservable() } }
      ]
    }).overrideComponent(ProductionCostRecordingPageComponent, { set: { template: `
      <ng-container *ngIf="showWorkerPanel; else assignmentBlocked">
        <section id="workerPanel">
          <p id="workerLoading" *ngIf="loadingWorkers">جارٍ تحميل التسكين الحالي وحالة الحضور…</p>
          <p id="workerError" *ngIf="workerContextError">{{ workerContextError }}</p>
          <p id="noCurrentWorkers" *ngIf="!loadingWorkers && !workerContextError && !currentWorkers.length">لا يوجد عمال مسكنون حاليًا لهذه المرحلة.</p>
          <p id="attendanceEmptyState" *ngIf="showAttendanceEmptyState">لا توجد بيانات حضور متاحة لهذا التاريخ.</p>
          <p id="attendanceProductionDateNote">المزامنة اليدوية تقرأ حضور تاريخ الإنتاج المحدد.</p>
          <button id="attendanceSyncAction" *ngIf="canSyncAttendance" (click)="syncAttendanceToday()">تحديث حضور تاريخ الإنتاج</button>
        </section>
      </ng-container>
      <ng-template #assignmentBlocked><p id="assignmentPlaceholder">اختر المرحلة الفرعية أولًا.</p></ng-template>
      <section id="readinessCard">
        <p id="readinessGuidance" *ngIf="!hasProductReadinessContext">اختر الخط والموديل وتاريخ الإنتاج لعرض الجاهزية.</p>
        <p id="readinessLoading" *ngIf="hasProductReadinessContext && productReadinessLoading">جارٍ حساب جاهزية مراحل المنتج لتاريخ الإنتاج المحدد…</p>
        <p id="readinessError" *ngIf="hasProductReadinessContext && productReadinessError">{{ productReadinessError }}</p>
        <p id="readinessSuccess" *ngIf="productReadiness">{{ productReadiness.productModelName }}</p>
      </section>
      <section id="financialPreview">
        <p id="previewTotal" *ngIf="preview && previewIsFresh">{{ preview.totalWorkerEarnings }}</p>
        <p id="previewInconsistent" *ngIf="preview && !previewIsFresh">{{ previewStaleMessage }}</p>
      </section>
      <section id="approvalCancellationDialog" *ngIf="productionApprovalCancellationDialogVisible">
        <p id="approvalCancellationRecord" *ngIf="pendingProductionApprovalCancellation as record">{{ record.stageName }} {{ record.totalWorkerEarnings }}</p>
        <p id="approvalCancellationReason">{{ productionApprovalCancellationForm.controls.reason.value }}</p>
      </section>
    ` } }).compileComponents();
    fixture = TestBed.createComponent(ProductionCostRecordingPageComponent);
    component = fixture.componentInstance;
  });

  it('filters draft, approved and cancelled records by order, date and status', () => {
    component.records = [draft('Draft'), { ...draft('Approved'), id: 'record-2', productionDate: '2026-07-14' }, { ...draft('Cancelled'), id: 'record-3' }];
    component.recordStatusFilter = 'Approved'; component.recordDateFilter = '2026-07-14'; component.recordOrderFilter = 'order-1';
    expect(component.filteredRecords().map(record => record.id)).toEqual(['record-2']);
  });

  it('approves only a draft when the user has production.approve and sends its concurrency token', () => {
    spyOn(window, 'confirm').and.returnValue(true); api.approve.and.returnValue(of(draft('Approved'))); component.records = [financiallyConsistentDraft()];
    component.approve(financiallyConsistentDraft());
    expect(api.approve).toHaveBeenCalledWith('record-1', 'token-1');
    permissions.values = ['production.view']; component.approve(financiallyConsistentDraft());
    expect(api.approve).toHaveBeenCalledTimes(1);
  });

  it('does not send duplicate approval requests while one is pending', () => {
    spyOn(window, 'confirm').and.returnValue(true); api.approve.and.returnValue(NEVER); component.approve(financiallyConsistentDraft()); component.approve(financiallyConsistentDraft());
    expect(api.approve).toHaveBeenCalledTimes(1);
  });

  it('renders the server total and blocks a financially inconsistent preview from becoming fresh', () => {
    const consistent = { ...draft(), totalWorkerEarnings: 190, workers: [{ workerId: 'worker-1', workerCode: 'W1', workerName: 'Worker', equivalentQuantity: 500, calculatedEarning: 190 }] };
    (component as any).setFreshPreview(consistent);
    fixture.detectChanges();

    expect(component.previewIsFresh).toBeTrue();
    expect(fixture.nativeElement.querySelector('#previewTotal')?.textContent).toContain('190');

    (component as any).setFreshPreview({ ...consistent, totalWorkerEarnings: 0 });
    fixture.detectChanges();

    expect(component.previewIsFresh).toBeFalse();
    expect(component.previewStaleMessage).toContain('إجمالي المستحقات لا يطابق مجموع مستحقات العمال');
    expect(fixture.nativeElement.querySelector('#previewInconsistent')?.textContent).toContain('نتيجة المعاينة غير متسقة');
  });

  it('shows production approval cancellation only for an approved record and requires an Arabic reason', () => {
    const approved = draft('Approved');
    component.openProductionApprovalCancellationDialog(approved);
    fixture.detectChanges();

    expect(component.productionApprovalCancellationDialogVisible).toBeTrue();
    expect(fixture.nativeElement.querySelector('#approvalCancellationRecord')?.textContent).toContain('Sew');

    component.confirmProductionApprovalCancellation();
    expect(api.cancelProductionApproval).not.toHaveBeenCalled();
    expect(component.error).toContain('سبب إلغاء اعتماد الإنتاج مطلوب');

    component.productionApprovalCancellationForm.controls.reason.setValue('تصحيح اعتماد الإنتاج');
    component.confirmProductionApprovalCancellation();

    expect(api.cancelProductionApproval).toHaveBeenCalledWith('record-1', 'token-1', 'تصحيح اعتماد الإنتاج');
  });

  it('prevents duplicate production approval cancellation and refreshes the cancelled status after success', () => {
    const approved = draft('Approved');
    const cancelled = { ...approved, status: 'Cancelled' as const, concurrencyToken: 'token-2', approvalCancellationReason: 'تصحيح اعتماد الإنتاج', approvalCancelledByUserId: 'user-1', approvalCancelledAtUtc: '2026-07-15T12:00:00Z' };
    component.records = [approved];
    component.openProductionApprovalCancellationDialog(approved);
    component.productionApprovalCancellationForm.controls.reason.setValue('تصحيح اعتماد الإنتاج');
    api.cancelProductionApproval.and.returnValue(NEVER);

    component.confirmProductionApprovalCancellation();
    component.confirmProductionApprovalCancellation();

    expect(api.cancelProductionApproval).toHaveBeenCalledTimes(1);

    component.saving = false;
    api.cancelProductionApproval.and.returnValue(of(cancelled));
    api.listRecords.and.returnValue(of([cancelled]));
    component.productionApprovalCancellationDialogVisible = true;
    component.pendingProductionApprovalCancellation = approved;
    component.confirmProductionApprovalCancellation();

    expect(component.records[0].status).toBe('Cancelled');
    expect(component.recordActionSuccess).toContain('بقي السجل');
    expect(component.productionApprovalCancellationDialogVisible).toBeFalse();
  });

  it('shows the required conflict message for a 409 response', () => {
    spyOn(window, 'confirm').and.returnValue(true); api.approve.and.returnValue(throwError(() => ({ status: 409 }))); component.approve(financiallyConsistentDraft());
    expect(component.error).toBe('تم تعديل السجل بواسطة مستخدم آخر. حدّث البيانات وحاول مرة أخرى.');
  });

  it('opens approved and cancelled records as read-only', () => {
    api.getRecord.and.returnValue(of(draft('Approved'))); component.openRecord(draft('Approved'));
    expect(component.recordForm.disabled).toBeTrue();
  });

  it('does not load dependent selectors until their parent selection is valid', () => {
    component.selectFactory('');
    expect(masterData.productionLines).not.toHaveBeenCalled();
    component.selectFactory('factory-1');
    expect(masterData.productionLines).toHaveBeenCalledTimes(1);
    expect(masterData.mainStagesForLine).not.toHaveBeenCalled();
    component.selectProductionLine('line-1');
    expect(masterData.mainStagesForLine).toHaveBeenCalledWith('line-1');
    expect(masterData.subStagesForMainStage).not.toHaveBeenCalled();
  });

  it('clears all stale descendants and production context when a parent changes', () => {
    component.selectedFactoryId = 'factory-1'; component.selectedProductionLineId = 'line-old'; component.selectedMainStageId = 'main-old'; component.selectedSubStageId = 'sub-old';
    component.recordForm.patchValue({ productionOrderId: 'order-old', productModelStageId: 'stage-old', producedQuantity: 20 });
    spyOn(window, 'confirm').and.returnValue(true);
    component.selectProductionLine('line-new');
    expect(component.selectedMainStageId).toBe('');
    expect(component.selectedSubStageId).toBe('');
    expect(component.recordForm.controls.productionOrderId.value).toBe('');
    expect(component.recordForm.controls.productModelStageId.value).toBe('');
    expect(masterData.mainStagesForLine).toHaveBeenCalledWith('line-new');
  });

  it('loads worker context only after a sub-stage is selected', () => {
    component.selectSubStage('');
    expect(assignments.getSubStageWorkerContext).not.toHaveBeenCalled();
    component.selectSubStage('sub-1');
    expect(assignments.getSubStageWorkerContext).toHaveBeenCalledWith('sub-1', component.selectedProductionDate);
  });

  it('shows only the assignment placeholder before selecting a sub-stage', () => {
    fixture.detectChanges();

    expect(fixture.nativeElement.querySelector('#assignmentPlaceholder')).not.toBeNull();
    expect(fixture.nativeElement.querySelector('#workerPanel')).toBeNull();
    expect(assignments.getSubStageWorkerContext).not.toHaveBeenCalled();
    expect(attendance.getForProductionDate).not.toHaveBeenCalled();
  });

  it('keeps the worker panel visible and shows loading after selecting a valid sub-stage', () => {
    assignments.getSubStageWorkerContext.and.returnValue(NEVER);

    component.selectSubStage('sub-1');
    fixture.detectChanges();

    expect(component.showWorkerPanel).toBeTrue();
    expect(fixture.nativeElement.querySelector('#workerPanel')).not.toBeNull();
    expect(fixture.nativeElement.querySelector('#workerLoading')).not.toBeNull();
    expect(fixture.nativeElement.querySelector('#assignmentPlaceholder')).toBeNull();
  });

  it('loads attendance for the selected production date without posting a manual sync', () => {
    component.selectSubStage('sub-1');

    expect(attendance.getForProductionDate).toHaveBeenCalledWith(component.selectedProductionDate);
    expect(attendance.syncForProductionDate).not.toHaveBeenCalled();
  });

  it('keeps the worker panel visible when attendance is empty or no workers are assigned', () => {
    component.selectSubStage('sub-1');
    fixture.detectChanges();

    expect(fixture.nativeElement.querySelector('#workerPanel')).not.toBeNull();
    expect(fixture.nativeElement.querySelector('#attendanceEmptyState')).not.toBeNull();
    expect(fixture.nativeElement.querySelector('#noCurrentWorkers')).not.toBeNull();
  });

  it('keeps the selected hierarchy and exposes an Arabic retry state when worker loading fails', () => {
    assignments.getSubStageWorkerContext.and.returnValue(throwError(() => ({ status: 500 })));

    component.selectSubStage('sub-1');
    fixture.detectChanges();

    expect(component.selectedSubStageId).toBe('sub-1');
    expect(fixture.nativeElement.querySelector('#workerPanel')).not.toBeNull();
    expect(fixture.nativeElement.querySelector('#workerError')?.textContent).toContain('تعذر تحميل العمال');
  });

  it('clears the worker panel when a parent changes and reloads it for the new sub-stage', () => {
    component.selectedFactoryId = 'factory-1';
    component.selectSubStage('sub-1');
    expect(component.showWorkerPanel).toBeTrue();

    spyOn(window, 'confirm').and.returnValue(true);
    component.selectProductionLine('line-2');

    expect(component.selectedSubStageId).toBe('');
    expect(component.workerContext).toBeNull();
    expect(component.showWorkerPanel).toBeFalse();

    component.selectSubStage('sub-2');
    expect(assignments.getSubStageWorkerContext).toHaveBeenCalledWith('sub-2', component.selectedProductionDate);
  });

  it('ignores a late worker response from the previously selected sub-stage', () => {
    const first = new Subject<ReturnType<typeof emptyWorkerContext>>();
    const second = new Subject<ReturnType<typeof emptyWorkerContext>>();
    assignments.getSubStageWorkerContext.and.returnValues(first, second);

    component.selectSubStage('sub-1');
    component.selectSubStage('sub-2');
    first.next(emptyWorkerContext('sub-1', 'worker-old'));
    expect(component.workerContext).toBeNull();

    second.next(emptyWorkerContext('sub-2', 'worker-new'));
    expect(component.workerContext?.subStageId).toBe('sub-2');
    expect(component.currentWorkers.map(worker => worker.workerId)).toEqual(['worker-new']);
  });

  it('shows the production-date sync action only to attendance.sync users', () => {
    component.selectSubStage('sub-1');
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('#attendanceSyncAction')).not.toBeNull();

    permissions.values = ['attendance.view'];
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('#attendanceSyncAction')).toBeNull();
  });

  it('syncs the selected production date once, refreshes attendance and selected-stage workers, and preserves the draft context', () => {
    component.selectedFactoryId = 'factory-1';
    component.selectedProductionLineId = 'line-1';
    component.selectedMainStageId = 'main-1';
    component.selectedSubStageId = 'sub-1';
    component.recordForm.patchValue({ productionOrderId: 'order-1', productModelStageId: 'stage-1', producedQuantity: 12 });

    component.syncAttendanceToday();

    expect(attendance.syncForProductionDate).toHaveBeenCalledWith(component.selectedProductionDate);
    expect(attendance.getForProductionDate).toHaveBeenCalledWith(component.selectedProductionDate);
    expect(assignments.getSubStageWorkerContext).toHaveBeenCalledWith('sub-1', component.selectedProductionDate);
    expect(component.selectedFactoryId).toBe('factory-1');
    expect(component.selectedProductionLineId).toBe('line-1');
    expect(component.selectedMainStageId).toBe('main-1');
    expect(component.selectedSubStageId).toBe('sub-1');
    expect(component.recordForm.controls.productionOrderId.value).toBe('order-1');
    expect(component.attendanceSuccess).toContain('تم تحديث حضور اليوم');
    expect(component.showWorkerPanel).toBeTrue();
  });

  it('prevents repeated manual sync submissions while the first request is pending', () => {
    attendance.syncForProductionDate.and.returnValue(NEVER);

    component.syncAttendanceToday();
    component.syncAttendanceToday();

    expect(attendance.syncForProductionDate).toHaveBeenCalledTimes(1);
    expect(component.attendanceSyncing).toBeTrue();
  });

  it('does not cancel an active manual sync when worker context refreshes', () => {
    const sync = new Subject<typeof syncResult>();
    attendance.syncForProductionDate.and.returnValue(sync);
    component.selectSubStage('sub-1');

    component.syncAttendanceToday();
    component.selectSubStage('sub-2');

    expect(attendance.syncForProductionDate).toHaveBeenCalledTimes(1);
    expect(component.attendanceSyncing).toBeTrue();
    sync.next(syncResult);
    sync.complete();
    expect(component.attendanceSyncing).toBeFalse();
  });

  it('reports the dedicated Arabic message after a client sync timeout and performs one safe read refresh', () => {
    component.selectedSubStageId = 'sub-1';
    attendance.syncForProductionDate.and.returnValue(throwError(() => new TimeoutError()));

    component.syncAttendanceToday();

    expect(component.attendanceError).toBe('انتهت مهلة مزامنة الحضور. تحقق من حالة المصدر ثم أعد المحاولة.');
    expect(attendance.getForProductionDate).toHaveBeenCalledTimes(1);
    expect(assignments.getSubStageWorkerContext).toHaveBeenCalledWith('sub-1', component.selectedProductionDate);
    expect(component.showWorkerPanel).toBeTrue();
  });

  it('distinguishes a backend sync failure without clearing the selected hierarchy', () => {
    component.selectedSubStageId = 'sub-1';
    attendance.syncForProductionDate.and.returnValue(throwError(() => ({ status: 500 })));

    component.syncAttendanceToday();

    expect(component.attendanceError).toBe('حدث خطأ بالخادم أثناء مزامنة الحضور. حاول مرة أخرى.');
    expect(component.selectedSubStageId).toBe('sub-1');
    expect(component.showWorkerPanel).toBeTrue();
  });

  it('handles forbidden sync safely with an Arabic permission message', () => {
    attendance.syncForProductionDate.and.returnValue(throwError(() => ({ status: 403 })));

    component.syncAttendanceToday();

    expect(component.attendanceError).toBe('لا تملك صلاحية تحديث الحضور الآن.');
    expect(component.attendanceSyncing).toBeFalse();
  });

  it('explains selected-date sync for a historical production date and explains empty attendance', () => {
    component.selectSubStage('sub-1');
    component.recordForm.controls.productionDate.setValue('2020-01-01');
    fixture.detectChanges();

    expect(component.isSelectedProductionDateToday).toBeFalse();
    expect(fixture.nativeElement.querySelector('#attendanceProductionDateNote')?.textContent).toContain('تاريخ الإنتاج المحدد');
    expect(fixture.nativeElement.querySelector('#attendanceEmptyState')?.textContent).toContain('لا توجد بيانات حضور');
  });

  it('uses the historical production date when refreshing a historical production draft', () => {
    component.selectedSubStageId = 'sub-1';
    component.recordForm.controls.productionDate.setValue('2020-01-01');

    component.syncAttendanceToday();

    expect(attendance.syncForProductionDate).toHaveBeenCalledWith('2020-01-01');
    expect(assignments.getSubStageWorkerContext).toHaveBeenCalledWith('sub-1', '2020-01-01');
    expect(component.attendanceSuccess).toContain('تم تحديث حضور تاريخ الإنتاج 2020-01-01');
  });

  it('restores a contextual factory-to-sub-stage route in parent-to-child order', () => {
    masterData.factories.and.returnValue(of([{ id: 'factory-1', code: 'F-1', name: 'Factory', isActive: true }]));
    masterData.productionLines.and.returnValue(of([{ id: 'line-1', factoryId: 'factory-1', lineCode: 'L-1', name: 'Line', sequenceOrder: 1, isActive: true }]));
    masterData.mainStagesForLine.and.returnValue(of([{ id: 'main-1', productionLineId: 'line-1', name: 'Main', sequenceOrder: 1, isCritical: false, isActive: true }]));
    masterData.subStagesForMainStage.and.returnValue(of([{ id: 'sub-1', mainStageId: 'main-1', code: 'SUB', name: 'Sub', capacity: 1, sequenceOrder: 1, isActive: true }]));
    routeQueryParams.next(convertToParamMap({ factoryId: 'factory-1', productionLineId: 'line-1', mainStageId: 'main-1', subStageId: 'sub-1' }));

    fixture.detectChanges();

    expect(component.selectedFactoryId).toBe('factory-1');
    expect(component.selectedProductionLineId).toBe('line-1');
    expect(component.selectedMainStageId).toBe('main-1');
    expect(component.selectedSubStageId).toBe('sub-1');
    expect(masterData.productionLines).toHaveBeenCalledTimes(1);
    expect(masterData.mainStagesForLine).toHaveBeenCalledWith('line-1');
    expect(masterData.subStagesForMainStage).toHaveBeenCalledWith('main-1');
    expect(assignments.getSubStageWorkerContext).toHaveBeenCalledWith('sub-1', component.selectedProductionDate);
  });

  it('keeps the nearest valid parent selection and explains unavailable routed context', () => {
    masterData.factories.and.returnValue(of([{ id: 'factory-1', code: 'F-1', name: 'Factory', isActive: true }]));
    masterData.productionLines.and.returnValue(of([{ id: 'line-2', factoryId: 'factory-1', lineCode: 'L-2', name: 'Other line', sequenceOrder: 1, isActive: true }]));
    routeQueryParams.next(convertToParamMap({ factoryId: 'factory-1', productionLineId: 'line-missing', mainStageId: 'main-1', subStageId: 'sub-1' }));

    fixture.detectChanges();

    expect(component.selectedFactoryId).toBe('factory-1');
    expect(component.selectedProductionLineId).toBe('');
    expect(component.error).toContain('خط الإنتاج المحدد');
    expect(component.productionLines.map(line => line.id)).toEqual(['line-2']);
  });

  it('rejects duplicate workers in the same production batch before submission', () => {
    component.selectedFactoryId = 'factory-1'; component.selectedProductionLineId = 'line-1'; component.selectedMainStageId = 'main-1'; component.selectedSubStageId = 'sub-1';
    component.orders = [{ id: 'order-1', orderNumber: 'O-1', productModelId: 'model-1', productModelCode: 'M-1', productionLineId: 'line-1', productionDate: '2026-07-13', plannedQuantity: 20, status: 'Active' }];
    component.modelStages = [{ id: 'stage-1', subStageId: 'sub-1', stageOrder: 1, piecePrice: 1, compensationMode: 'SharedPercentage', isRequired: true, isActive: true }];
    component.workerContext = { subStageId: 'sub-1', currentWorkers: [{ workerId: 'worker-1', employeeCode: 'W1', fullName: 'Worker', attendanceStatus: 'Present', attendanceTimeUtc: null, assignmentType: 'Default', effectiveSubStageId: 'sub-1', isAvailable: true }], presentWorkers: [], availableWorkers: [], unavailableWorkersCount: 0 };
    component.recordForm.patchValue({ productionOrderId: 'order-1', productModelStageId: 'stage-1' });
    component.addWorker(component.currentWorkers[0]);
    component.addWorker(component.currentWorkers[0]);
    expect(component.workers.length).toBe(1);
    expect(component.error).toContain('أكثر من مرة');
  });

  it('edits a current assignment inside the workflow and respects assignment permission', () => {
    component.selectedSubStageId = 'sub-1';
    component.workerContext = { subStageId: 'sub-1', currentWorkers: [], presentWorkers: [{ workerId: 'worker-1', employeeCode: 'W1', fullName: 'Worker', attendanceStatus: 'Present', attendanceTimeUtc: null, assignmentType: null, effectiveSubStageId: null, isAvailable: true }], availableWorkers: [], unavailableWorkersCount: 0 };
    component.assignmentForm.patchValue({ workerId: 'worker-1' });
    component.applyAssignment();
    expect(assignments.createDefaultAssignment).toHaveBeenCalledWith({ workerId: 'worker-1', subStageId: 'sub-1' });
    permissions.values = ['assignments.view'];
    component.assignmentForm.patchValue({ workerId: 'worker-1' });
    component.applyAssignment();
    expect(assignments.createDefaultAssignment).toHaveBeenCalledTimes(1);
  });

  it('keeps only permanent assignments in the active worker list', () => {
    component.workerContext = {
      subStageId: 'sub-1',
      currentWorkers: [
        { workerId: 'permanent', employeeCode: 'P1', fullName: 'Permanent', attendanceStatus: 'Present', attendanceTimeUtc: null, assignmentType: 'Default', effectiveSubStageId: 'sub-1', isAvailable: true },
        { workerId: 'historical-temporary', employeeCode: 'T1', fullName: 'Historical', attendanceStatus: 'Present', attendanceTimeUtc: null, assignmentType: 'Temporary', effectiveSubStageId: 'sub-1', isAvailable: true }
      ],
      presentWorkers: [], availableWorkers: [], unavailableWorkersCount: 0
    };

    expect(component.currentWorkers.map(worker => worker.workerId)).toEqual(['permanent']);
  });

  it('keeps an absent assigned worker visible but excludes them from production participants', () => {
    component.workerContext = {
      subStageId: 'sub-1',
      currentWorkers: [
        { workerId: 'present-1', employeeCode: 'P1', fullName: 'Present', attendanceStatus: 'Present', attendanceTimeUtc: null, assignmentType: 'Default', effectiveSubStageId: 'sub-1', isAvailable: true },
        { workerId: 'absent-1', employeeCode: 'A1', fullName: 'Absent', attendanceStatus: 'Absent', attendanceTimeUtc: null, assignmentType: 'Default', effectiveSubStageId: 'sub-1', isAvailable: false }
      ],
      presentWorkers: [], availableWorkers: [], unavailableWorkersCount: 1
    };
    expect(component.currentWorkers.length).toBe(2);
    expect(component.recordingWorkers.map(worker => worker.workerId)).toEqual(['present-1']);
  });

  it('restores a draft hierarchy in parent-to-child order without clearing its participants', () => {
    component.orders = [{ id: 'order-1', orderNumber: 'O-1', productModelId: 'model-1', productModelCode: 'M-1', productionLineId: 'line-1', productionDate: '2026-07-13', plannedQuantity: 20, status: 'Active' }];
    api.getRecord.and.returnValue(of({ ...draft(), workers: [{ workerId: 'worker-1', workerCode: 'W1', workerName: 'Worker', percentage: 100, equivalentQuantity: 10, calculatedEarning: 10 }] }));
    masterData.factories.and.returnValue(of([{ id: 'factory-1', code: 'F-1', name: 'Factory', isActive: true }]));
    masterData.allProductionLines.and.returnValue(of([{ id: 'line-1', factoryId: 'factory-1', lineCode: 'L-1', name: 'Line', sequenceOrder: 1, isActive: true }]));
    masterData.allMainStages.and.returnValue(of([{ id: 'main-1', productionLineId: 'line-1', name: 'Main', sequenceOrder: 1, isCritical: false, isActive: true }]));
    masterData.allSubStages.and.returnValue(of([{ id: 'sub-1', mainStageId: 'main-1', code: 'SUB', name: 'Sub', capacity: 1, sequenceOrder: 1, isActive: true }]));
    api.listModelStages.and.returnValue(of([{ id: 'stage-1', subStageId: 'sub-1', stageOrder: 1, piecePrice: 1, compensationMode: 'SharedPercentage', isRequired: true, isActive: true }]));

    component.openRecord(draft());

    expect(component.selectedFactoryId).toBe('factory-1');
    expect(component.selectedProductionLineId).toBe('line-1');
    expect(component.selectedMainStageId).toBe('main-1');
    expect(component.selectedSubStageId).toBe('sub-1');
    expect(component.recordForm.controls.productionOrderId.value).toBe('order-1');
    expect(component.recordForm.controls.clientRequestId.value).toMatch(/^[0-9a-f]{8}-[0-9a-f]{4}-4[0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/i);
    expect(component.workers.length).toBe(1);
    expect(component.draftContextUnavailable).toBe('');
    expect(api.getProductReadiness).toHaveBeenCalledTimes(1);
    expect(api.getProductReadiness).toHaveBeenCalledWith('model-1', 'line-1', '2026-07-13');
  });

  it('shows readiness loading and then the restored Draft summary without manually reselecting the model', () => {
    const readinessResponse = new Subject<ProductProductionReadiness>();
    api.getProductReadiness.and.returnValue(readinessResponse);
    fixture.detectChanges();
    configureRestorableDraftContext(component, api, masterData);

    component.openRecord(draft());
    fixture.detectChanges();

    expect(fixture.nativeElement.querySelector('#readinessLoading')).not.toBeNull();
    expect(api.getProductReadiness).toHaveBeenCalledTimes(1);

    readinessResponse.next(readiness());
    readinessResponse.complete();
    fixture.detectChanges();

    expect(fixture.nativeElement.querySelector('#readinessSuccess')?.textContent).toContain('Model');
  });

  it('uses the same readiness loader for interactive model selection and refreshes it for a changed production date', () => {
    component.ngOnInit();
    component.selectedFactoryId = 'factory-1';
    component.selectedProductionLineId = 'line-1';
    component.orderForm.controls.productModelId.setValue('model-1');

    expect(api.getProductReadiness).toHaveBeenCalledWith('model-1', 'line-1', component.selectedProductionDate);

    component.selectedSubStageId = 'sub-1';
    component.recordForm.controls.productionDate.setValue('2026-07-14');

    expect(api.getProductReadiness).toHaveBeenCalledWith('model-1', 'line-1', '2026-07-14');
  });

  it('clears the old summary and reloads readiness after the production line context changes', () => {
    fixture.detectChanges();
    component.selectedFactoryId = 'factory-1';
    component.selectedProductionLineId = 'line-1';
    component.orderForm.controls.productModelId.setValue('model-1');
    expect(component.productReadiness).not.toBeNull();

    spyOn(window, 'confirm').and.returnValue(true);
    component.selectProductionLine('line-2');
    expect(component.productReadiness).toBeNull();

    component.orderForm.controls.productModelId.setValue('model-2');
    expect(api.getProductReadiness).toHaveBeenCalledWith('model-2', 'line-2', component.selectedProductionDate);
  });

  it('ignores a stale readiness response from a previous Draft context', () => {
    const firstResponse = new Subject<ProductProductionReadiness>();
    const secondResponse = new Subject<ProductProductionReadiness>();
    api.getProductReadiness.and.returnValues(firstResponse, secondResponse);
    component.selectedFactoryId = 'factory-1';
    component.selectedProductionLineId = 'line-1';
    component.orders = [{ id: 'order-1', orderNumber: 'O-1', productModelId: 'model-1', productModelCode: 'M-1', productionLineId: 'line-1', productionDate: '2026-07-13', plannedQuantity: 20, status: 'Active' }];
    component.recordForm.patchValue({ productionOrderId: 'order-1', productionDate: '2026-07-13' }, { emitEvent: false });

    (component as any).refreshProductReadiness();
    component.recordForm.patchValue({ productionDate: '2026-07-14' }, { emitEvent: false });
    (component as any).refreshProductReadiness();
    firstResponse.next({ ...readiness('2026-07-13'), productModelName: 'Previous Draft' });
    secondResponse.next({ ...readiness('2026-07-14'), productModelName: 'Current Draft' });

    expect(component.productReadiness?.productModelName).toBe('Current Draft');
    expect(component.productReadiness?.productionDate).toBe('2026-07-14');
  });

  it('keeps the Draft open and shows an Arabic retry state when readiness loading fails', () => {
    api.getProductReadiness.and.returnValue(throwError(() => ({ status: 500 })));
    fixture.detectChanges();
    configureRestorableDraftContext(component, api, masterData);

    component.openRecord(draft());
    fixture.detectChanges();

    expect(component.editingRecordId).toBe('record-1');
    expect(component.draftContextUnavailable).toBe('');
    expect(component.productReadinessError).toContain('تعذر تحميل ملخص الجاهزية');
    expect(fixture.nativeElement.querySelector('#readinessError')?.textContent).toContain('تبقى المسودة مفتوحة');
  });

  it('shows guidance rather than a blank readiness card when the required context is missing', () => {
    fixture.detectChanges();

    expect(fixture.nativeElement.querySelector('#readinessGuidance')?.textContent).toContain('اختر الخط والموديل وتاريخ الإنتاج');
    expect(fixture.nativeElement.querySelector('#readinessLoading')).toBeNull();
    expect(fixture.nativeElement.querySelector('#readinessSuccess')).toBeNull();
  });

  it('keeps a draft visible and blocks saving when referenced master data is unavailable', () => {
    component.orders = [{ id: 'order-1', orderNumber: 'O-1', productModelId: 'model-1', productModelCode: 'M-1', productionLineId: 'line-1', productionDate: '2026-07-13', plannedQuantity: 20, status: 'Active' }];
    api.getRecord.and.returnValue(of(draft()));
    api.listModelStages.and.returnValue(of([]));

    component.openRecord(draft());

    expect(component.editingRecordId).toBe('record-1');
    expect(component.draftContextUnavailable).toContain('تعذر استعادة مسار');
  });

  it('unassigns a permanent current worker with a mandatory reason', () => {
    component.selectedFactoryId = 'factory-1'; component.selectedSubStageId = 'sub-1';
    const worker = { workerId: 'worker-1', employeeCode: 'W1', fullName: 'Worker', attendanceStatus: 'Present' as const, attendanceTimeUtc: null, assignmentId: 'assignment-1', assignmentType: 'Default' as const, effectiveSubStageId: 'sub-1', isAvailable: true };
    component.openUnassignDialog(worker);
    component.assignmentForm.controls.reason.setValue('انتهاء الوردية');
    component.confirmUnassign();
    expect(assignments.removeDefaultAssignment).toHaveBeenCalledWith('worker-1', 'sub-1', 'انتهاء الوردية');

  });

  it('does not send an invalid draft and explains the missing Arabic requirements', () => {
    component.saveDraft();

    expect(api.createDraft).not.toHaveBeenCalled();
    expect(component.error).toContain('أمر الإنتاج مطلوب');
    expect(component.error).toContain('الكمية المنتجة مطلوبة');
  });

  it('invalidates a current preview when quantity or permanent unassignment changes the unsaved batch', () => {
    configureValidDraft(component);
    const currentPreview = { ...draft(), workers: [{ workerId: 'worker-1', workerCode: 'W1', workerName: 'Worker 1', percentage: 100, equivalentQuantity: 10, calculatedEarning: 10 }] };
    api.calculatePreview.and.returnValue(of(currentPreview));

    component.calculatePreview();
    expect(component.previewIsFresh).toBeTrue();

    component.recordForm.controls.producedQuantity.setValue(11);
    expect(component.preview).toBeNull();
    expect(component.previewIsFresh).toBeFalse();
    expect(component.previewIsStale).toBeTrue();
    expect(component.previewStaleMessage).toBe('تم تغيير بيانات الدفعة. أعد حساب المعاينة.');

    // Restore a fresh preview then exercise the same lifecycle through a removed participant.
    component.recordForm.controls.producedQuantity.setValue(10);
    component.calculatePreview();
    (component as any).assignmentDraftUpdateMode = 'draft-too';
    (component as any).applyDraftParticipantImpact('worker-1', 'worker-2');
    expect(component.workers.at(0).get('workerId')?.value).toBe('worker-2');
    expect(component.preview).toBeNull();
    expect(component.previewIsStale).toBeTrue();

    component.calculatePreview();
    (component as any).assignmentDraftUpdateMode = 'draft-too';
    (component as any).applyDraftParticipantImpact('worker-2');
    expect(component.workers.length).toBe(0);
    expect(component.preview).toBeNull();
    expect(component.previewIsStale).toBeTrue();
  });

  it('sends only current participants after a fresh recalculation and prevents duplicate draft saves while pending', () => {
    configureValidDraft(component);
    api.calculatePreview.and.returnValue(of({ ...draft(), workers: [{ workerId: 'worker-1', workerCode: 'W1', workerName: 'Worker 1', percentage: 100, equivalentQuantity: 10, calculatedEarning: 10 }] }));
    component.calculatePreview();
    (component as any).assignmentDraftUpdateMode = 'draft-too';
    (component as any).applyDraftParticipantImpact('worker-1', 'worker-2');

    api.calculatePreview.and.returnValue(of({ ...draft(), workers: [{ workerId: 'worker-2', workerCode: 'W2', workerName: 'Worker 2', percentage: 100, equivalentQuantity: 10, calculatedEarning: 10 }] }));
    component.calculatePreview();
    expect(component.preview?.workers.map(worker => worker.workerId)).toEqual(['worker-2']);

    api.createDraft.and.returnValue(NEVER);
    component.saveDraft();
    component.saveDraft();

    expect(api.createDraft).toHaveBeenCalledTimes(1);
    expect((api.createDraft.calls.mostRecent().args[0] as { workers: Array<{ workerId: string }> }).workers.map(worker => worker.workerId)).toEqual(['worker-2']);
  });
});

function configureRestorableDraftContext(
  component: ProductionCostRecordingPageComponent,
  api: jasmine.SpyObj<ProductionCostRecordingApiService>,
  masterData: jasmine.SpyObj<ManufacturingMasterDataApiService>
): void {
  component.orders = [{ id: 'order-1', orderNumber: 'O-1', productModelId: 'model-1', productModelCode: 'M-1', productionLineId: 'line-1', productionDate: '2026-07-13', plannedQuantity: 20, status: 'Active' }];
  api.getRecord.and.returnValue(of(draftRecord()));
  masterData.factories.and.returnValue(of([{ id: 'factory-1', code: 'F-1', name: 'Factory', isActive: true }]));
  masterData.allProductionLines.and.returnValue(of([{ id: 'line-1', factoryId: 'factory-1', lineCode: 'L-1', name: 'Line', sequenceOrder: 1, isActive: true }]));
  masterData.allMainStages.and.returnValue(of([{ id: 'main-1', productionLineId: 'line-1', name: 'Main', sequenceOrder: 1, isCritical: false, isActive: true }]));
  masterData.allSubStages.and.returnValue(of([{ id: 'sub-1', mainStageId: 'main-1', code: 'SUB', name: 'Sub', capacity: 1, sequenceOrder: 1, isActive: true }]));
  api.listModelStages.and.returnValue(of([{ id: 'stage-1', subStageId: 'sub-1', stageOrder: 1, piecePrice: 1, compensationMode: 'SharedPercentage', isRequired: true, isActive: true }]));
}

function draftRecord(): StageProductionRecord {
  return {
    id: 'record-1', productionOrderId: 'order-1', productModelStageId: 'stage-1', productionDate: '2026-07-13', producedQuantity: 10, acceptedQuantity: 10, rejectedQuantity: 0,
    status: 'Draft', stageCode: 'SEW', stageName: 'Sew', productModelCode: 'M-1', productModelName: 'Model', factoryCode: 'F-1', factoryName: 'Factory', productionLineCode: 'L-1', productionLineName: 'Line', mainStageName: 'Main', piecePrice: 1,
    compensationMode: 'SharedPercentage', totalWorkerEarnings: 10, concurrencyToken: 'token-1', workers: []
  };
}

function configureValidDraft(component: ProductionCostRecordingPageComponent): void {
  component.ngOnInit();
  component.selectedFactoryId = 'factory-1';
  component.selectedProductionLineId = 'line-1';
  component.selectedMainStageId = 'main-1';
  component.selectedSubStageId = 'sub-1';
  component.orders = [{ id: 'order-1', orderNumber: 'O-1', productModelId: 'model-1', productModelCode: 'M-1', productionLineId: 'line-1', productionDate: '2026-07-15', plannedQuantity: 20, status: 'Active' }];
  component.modelStages = [{ id: 'stage-1', subStageId: 'sub-1', stageOrder: 1, piecePrice: 1, compensationMode: 'SharedPercentage', isRequired: true, isActive: true }];
  component.recordForm.patchValue({ productionOrderId: 'order-1', productModelStageId: 'stage-1', productionDate: '2026-07-15', producedQuantity: 10, acceptedQuantity: 10, rejectedQuantity: 0 });
  component.workerContext = {
    subStageId: 'sub-1',
    currentWorkers: [
      { workerId: 'worker-1', employeeCode: 'W1', fullName: 'Worker 1', attendanceStatus: 'Present', attendanceTimeUtc: null, assignmentType: 'Default', effectiveSubStageId: 'sub-1', isAvailable: true },
      { workerId: 'worker-2', employeeCode: 'W2', fullName: 'Worker 2', attendanceStatus: 'Present', attendanceTimeUtc: null, assignmentType: 'Default', effectiveSubStageId: 'sub-1', isAvailable: true }
    ],
    presentWorkers: [], availableWorkers: [], unavailableWorkersCount: 0
  };
  component.workers.push((component as any).workerGroup('worker-1', 100, null, ''));
}
