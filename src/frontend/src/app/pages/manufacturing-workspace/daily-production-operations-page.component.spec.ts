import { Observable, Subject, of, throwError } from 'rxjs';
import { HttpClientTestingModule } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { NoopAnimationsModule } from '@angular/platform-browser/animations';
import { DailyProductionOperationsPageComponent } from './daily-production-operations-page.component';
import { DailyProductionPreview } from '../../core/services/production-cost-recording-api.service';
import { ProductionCostRecordingApiService } from '../../core/services/production-cost-recording-api.service';
import { ManufacturingMasterDataApiService } from '../../core/services/manufacturing-master-data-api.service';
import { AttendanceApiService } from '../../core/services/attendance-api.service';
import { PermissionService } from '../../core/services/permission.service';
import { FormSubmissionValidationService } from '../../shared/forms/form-submission-validation.service';
import { ManufacturingWorkspaceModule } from './manufacturing-workspace.module';

describe('DailyProductionOperationsPageComponent unified preview', () => {
  let component: DailyProductionOperationsPageComponent;
  let production: jasmine.SpyObj<any>;
  let masterData: jasmine.SpyObj<any>;
  let attendance: jasmine.SpyObj<any>;

  const preview: DailyProductionPreview = {
    productionDate: '2026-07-16',
    lineQuantity: 500,
    previewToken: 'preview-token',
    totalWorkerEntitlements: 250,
    stages: [{
      productModelStageId: 'stage-1', stageCode: 'S1', stageName: 'مرحلة 1', stageQuantity: 500,
      stageCost: 250, compensationMode: 'SharedPercentage', warnings: [],
      workers: [{ workerId: 'worker-1', workerCode: 'W1', workerName: 'عامل 1', percentage: 100, equivalentQuantity: 500, calculatedEarning: 250 }]
    }],
    workerTotals: [{ workerId: 'worker-1', workerCode: 'W1', workerName: 'عامل 1', totalEntitlement: 250 }],
    warnings: []
  };

  beforeEach(() => {
    production = jasmine.createSpyObj('ProductionCostRecordingApiService', ['previewDailyOperations', 'loadDailyOperations', 'saveDailyDraft']);
    masterData = jasmine.createSpyObj('ManufacturingMasterDataApiService', ['factories', 'allProductionLines', 'models']);
    attendance = jasmine.createSpyObj('AttendanceApiService', ['syncForProductionDate']);
    component = new DailyProductionOperationsPageComponent(
      masterData,
      attendance,
      production,
      { hasPermission: () => true } as any,
      { serverMessage: (error: any, fallback: string) => error?.error?.detail ?? fallback } as any
    );
    component.operations = {
      factoryId: 'factory-1', factoryName: 'Factory', productionLineId: 'line-1', productionLineName: 'Line',
      productModelId: 'model-1', productModelCode: 'M1', productModelName: 'Model', productionDate: '2026-07-16',
      staffingContextVersion: 'context', totalStages: 1, readyStages: 1, stagesWithAbsentWorkers: 0,
      stagesWithNoSourceCheckIn: 0, stagesWithoutStaffing: 0, stagesRequiringCostReview: 0,
      activeWorkers: [], stages: []
    };
    component.selectedFactoryId = 'factory-1';
    component.selectedProductionLineId = 'line-1';
    component.selectedProductModelId = 'model-1';
    component.productionDate = '2026-07-16';
    component.lineQuantity = 500;
    component.stages = [{
      productModelStageId: 'stage-1', subStageId: 'sub-stage-1', mainStageName: 'Main', stageCode: 'S1',
      stageName: 'مرحلة 1', stageOrder: 1, piecePrice: .5, compensationMode: 'SharedPercentage',
      staffingStatus: 'Staffed', attendanceStatus: 'Ready', hasAbsentWorkers: false, hasNoSourceCheckInWorkers: false,
      isFinancialReviewPending: false, isReady: true, originalWorkerIds: ['worker-1'],
      workers: [{ workerId: 'worker-1', workerCode: 'W1', workerName: 'عامل 1', isOnActiveService: true,
        effectiveAssignmentType: 'Permanent', attendanceStatus: 'Present', hasSourceCheckIn: true, isPresent: true,
        requiresAuthorizedOverride: false, suggestedPercentage: 100, percentage: 100, fixedAmount: null, notes: '', manualOverrideReason: '' }]
    } as any];
  });

  it('starts attendance synchronization only from the explicit manual action and blocks duplicate clicks', () => {
    const pending = new Subject<any>();
    masterData.factories.and.returnValue(of([]));
    attendance.syncForProductionDate.and.returnValue(pending);

    component.ngOnInit();
    expect(attendance.syncForProductionDate).not.toHaveBeenCalled();

    component.synchronizeAttendance();
    component.synchronizeAttendance();

    expect(attendance.syncForProductionDate).toHaveBeenCalledTimes(1);
    expect(component.attendanceSyncing).toBeTrue();
    pending.complete();
    expect(component.attendanceSyncing).toBeFalse();
  });

  it('sends exactly one request for repeated clicks while preview is active and renders its response without a page reload', () => {
    const pending = new Subject<DailyProductionPreview>();
    production.previewDailyOperations.and.returnValue(pending.asObservable());

    component.calculatePreview();
    component.calculatePreview();

    expect(production.previewDailyOperations).toHaveBeenCalledTimes(1);
    expect(masterData.factories).not.toHaveBeenCalled();
    expect(production.loadDailyOperations).not.toHaveBeenCalled();
    pending.next(preview);
    pending.complete();

    expect(component.preview).toEqual(preview);
    expect(component.previewing).toBeFalse();
  });

  it('does not cancel the active preview when a material UI change invalidates it', () => {
    let teardownCalls = 0;
    production.previewDailyOperations.and.returnValue(new Observable<DailyProductionPreview>(() => () => teardownCalls++));

    component.calculatePreview();
    component.lineQuantity = 501;
    component.lineQuantityChanged();

    expect(teardownCalls).toBe(0);
    expect(component.previewing).toBeTrue();
  });

  it('keeps the same ClientRequestId when a failed preview is retried with unchanged inputs', () => {
    production.previewDailyOperations.and.returnValues(
      throwError(() => ({ error: { detail: 'تعذر الاتصال بالخادم.' } })),
      of(preview)
    );

    component.calculatePreview();
    expect(component.error).toBe('تعذر الاتصال بالخادم.');
    component.calculatePreview();

    const firstRequest = production.previewDailyOperations.calls.argsFor(0)[0];
    const retryRequest = production.previewDailyOperations.calls.argsFor(1)[0];
    expect(firstRequest.clientRequestId).toBe(retryRequest.clientRequestId);
    expect(component.preview).toEqual(preview);
  });
});

describe('DailyProductionOperationsPageComponent visual hierarchy', () => {
  it('uses contained stage scrolling and structured stage, worker, preview, and entitlement regions', () => {
    const production = jasmine.createSpyObj('ProductionCostRecordingApiService', ['previewDailyOperations', 'loadDailyOperations', 'saveDailyDraft']);
    const masterData = jasmine.createSpyObj('ManufacturingMasterDataApiService', ['factories', 'allProductionLines', 'models']);
    const attendance = jasmine.createSpyObj('AttendanceApiService', ['syncForProductionDate']);
    masterData.factories.and.returnValue(of([]));

    TestBed.configureTestingModule({
      imports: [ManufacturingWorkspaceModule, HttpClientTestingModule, NoopAnimationsModule],
      providers: [
        { provide: ProductionCostRecordingApiService, useValue: production },
        { provide: ManufacturingMasterDataApiService, useValue: masterData },
        { provide: AttendanceApiService, useValue: attendance },
        { provide: PermissionService, useValue: { hasPermission: () => true } },
        { provide: FormSubmissionValidationService, useValue: { serverMessage: (_: unknown, fallback: string) => fallback } }
      ]
    });

    const fixture = TestBed.createComponent(DailyProductionOperationsPageComponent);
    const component = fixture.componentInstance;
    const stage = {
      productModelStageId: 'stage-1', subStageId: 'sub-stage-1', mainStageName: 'التجميع', stageCode: 'ST-01',
      stageName: 'تجميع الكتف', stageOrder: 1, piecePrice: .5, compensationMode: 'SharedPercentage',
      staffingStatus: 'Staffed', attendanceStatus: 'Ready', hasAbsentWorkers: false, hasNoSourceCheckInWorkers: false,
      isFinancialReviewPending: false, isReady: true, originalWorkerIds: ['worker-1'],
      workers: [{ workerId: 'worker-1', workerCode: 'W-001', workerName: 'Worker One', isOnActiveService: true,
        effectiveAssignmentType: 'Default', attendanceStatus: 'Present', hasSourceCheckIn: true, isPresent: true,
        requiresAuthorizedOverride: false, suggestedPercentage: 100, percentage: 100, fixedAmount: null, notes: '', manualOverrideReason: '' }]
    } as any;
    component.operations = {
      factoryId: 'factory-1', factoryName: 'مصنع القاهرة', productionLineId: 'line-1', productionLineName: 'خط التجميع',
      productModelId: 'model-1', productModelCode: 'M-01', productModelName: 'موديل اختبار', productionDate: '2026-07-17',
      staffingContextVersion: 'context', totalStages: 24, readyStages: 24, stagesWithAbsentWorkers: 0,
      stagesWithNoSourceCheckIn: 0, stagesWithoutStaffing: 0, stagesRequiringCostReview: 0, activeWorkers: [], stages: []
    };
    component.stages = Array.from({ length: 24 }, (_, index) => ({
      ...stage,
      productModelStageId: `stage-${index + 1}`,
      stageCode: `ST-${String(index + 1).padStart(2, '0')}`,
      stageName: index === 0
        ? 'مرحلة تجميع طويلة لاختبار التفاف الاسم داخل البطاقة دون قص أو تداخل'
        : `مرحلة ${index + 1}`,
      stageOrder: index + 1
    }));
    component.selectedStageId = 'stage-1';
    component.preview = {
      productionDate: '2026-07-17', lineQuantity: 500, previewToken: 'preview-token', totalWorkerEntitlements: 250,
      stages: [{ productModelStageId: 'stage-1', stageCode: 'ST-01', stageName: 'تجميع الكتف', stageQuantity: 500,
        stageCost: 250, compensationMode: 'SharedPercentage', warnings: [], workers: [{ workerId: 'worker-1', workerCode: 'W-001',
          workerName: 'Worker One', percentage: 100, equivalentQuantity: 500, calculatedEarning: 250 }] }],
      workerTotals: [{ workerId: 'worker-1', workerCode: 'W-001', workerName: 'Worker One', totalEntitlement: 250 }],
      warnings: []
    };
    fixture.detectChanges();

    const text = fixture.nativeElement.textContent as string;
    const stageButtons = fixture.nativeElement.querySelectorAll('.daily-production-operations__stage') as NodeListOf<HTMLButtonElement>;
    const workerTotalRow = fixture.nativeElement.querySelector('.daily-production-operations__worker-totals tbody tr') as HTMLTableRowElement;

    expect(fixture.nativeElement.querySelector('.plp-bounded-workspace--viewport')).not.toBeNull();
    expect(fixture.nativeElement.querySelector('.plp-bounded-workspace__panel--viewport')).not.toBeNull();
    const stageList = fixture.nativeElement.querySelector('.daily-production-operations__stage-list.plp-contained-scroll-list') as HTMLElement;
    expect(stageList).not.toBeNull();
    expect(getComputedStyle(stageList).overflowY).toBe('auto');
    expect(stageButtons).toHaveSize(24);
    expect(stageButtons[23].textContent).toContain('مرحلة 24');
    const firstStage = stageButtons[0];
    const secondStage = stageButtons[1];
    const stageSecondaryRow = firstStage.querySelector('.plp-entity-secondary-row') as HTMLElement;
    const firstStageRect = firstStage.getBoundingClientRect();
    const secondStageRect = secondStage.getBoundingClientRect();
    const secondaryRowRect = stageSecondaryRow.getBoundingClientRect();
    const firstStageStyle = getComputedStyle(firstStage);

    expect(firstStage.querySelector('plp-responsive-entity-row')).not.toBeNull();
    expect(firstStage.classList).toContain('plp-structured-card--content-sized');
    expect(firstStage.style.blockSize).toBe('');
    expect(firstStageStyle.position).not.toBe('absolute');
    expect(firstStageStyle.minBlockSize).not.toBe('0px');
    expect(firstStageStyle.overflow).toBe('visible');
    expect(stageSecondaryRow).not.toBeNull();
    expect(stageSecondaryRow.querySelectorAll('plp-status-badge')).toHaveSize(2);
    expect(firstStage.querySelector('[plp-entity-status]')).toBeNull();
    expect(secondaryRowRect.bottom)
      .withContext(JSON.stringify({ card: firstStageRect.toJSON(), secondary: secondaryRowRect.toJSON(), display: firstStageStyle.display, blockSize: firstStageStyle.blockSize }))
      .toBeLessThanOrEqual(firstStageRect.bottom + 1);
    expect(secondStageRect.top).toBeGreaterThanOrEqual(firstStageRect.bottom);
    expect(firstStage.classList).toContain('is-selected');

    [800, 1280, 1440].forEach((viewportWidth) => {
      stageList.style.inlineSize = `${viewportWidth}px`;
      const cardRect = firstStage.getBoundingClientRect();
      const metadataRect = stageSecondaryRow.getBoundingClientRect();
      const nextCardRect = secondStage.getBoundingClientRect();

      expect(metadataRect.left).withContext(`viewport ${viewportWidth}`).toBeGreaterThanOrEqual(cardRect.left - 1);
      expect(metadataRect.right).withContext(`viewport ${viewportWidth}`).toBeLessThanOrEqual(cardRect.right + 1);
      expect(metadataRect.bottom).withContext(`viewport ${viewportWidth}`).toBeLessThanOrEqual(cardRect.bottom + 1);
      expect(nextCardRect.top).withContext(`viewport ${viewportWidth}`).toBeGreaterThanOrEqual(cardRect.bottom);
    });
    expect(fixture.nativeElement.querySelector('.daily-production-operations__worker plp-responsive-entity-row')).not.toBeNull();
    expect(fixture.nativeElement.querySelector('.daily-production-operations__preview-stage plp-responsive-entity-row')).not.toBeNull();
    expect(fixture.nativeElement.querySelector('.daily-production-operations__worker-totals p-table')).not.toBeNull();
    expect(workerTotalRow.querySelector('.plp-responsive-entity-row__title')?.textContent).toContain('Worker One');
    expect(workerTotalRow.querySelector('.plp-responsive-entity-row__code')?.textContent).toContain('W-001');
    expect(workerTotalRow.querySelector('.daily-production-operations__entitlement')?.textContent).toContain('250.00');
    expect(text).not.toContain('SharedPercentage');
    expect(text).not.toContain('Default');
    expect(text).not.toContain('Ready');
  });
});
