import { HttpErrorResponse } from '@angular/common/http';
import { of, throwError } from 'rxjs';
import { PERMISSIONS } from '../../core/config/permission-identifiers';
import { PermissionService } from '../../core/services/permission.service';
import { FinancialReportResult, ProductionFinancialReportApiService } from '../../core/services/production-financial-report-api.service';
import { ProductionQuantitiesReportApiService, QuantitiesReportResult } from '../../core/services/production-quantities-report-api.service';
import { ManufacturingMasterDataApiService } from '../../core/services/manufacturing-master-data-api.service';
import { ProductionCostRecordingApiService } from '../../core/services/production-cost-recording-api.service';
import { ReportsWorkspacePageComponent } from './reports-workspace-page.component';
import { ReportsWorkspaceStateService } from './reports-workspace-state.service';

describe('ReportsWorkspacePageComponent', () => {
  const result: QuantitiesReportResult = {
    summary: {
      totalPhysicalProducedQuantity: 480,
      totalPhysicalAcceptedQuantity: 450,
      totalPhysicalRejectedQuantity: 30,
      totalStageProducedQuantity: 480,
      totalAcceptedQuantity: 450,
      totalRejectedQuantity: 30,
      recordCount: 3,
      stageCount: 2,
      workerCount: 4
    },
    rows: [{
      source: { sourceType: 'StageProductionRecord', stageProductionRecordId: 'record-1', productModelStageId: 'stage-1' },
      productionDate: '2026-07-18',
      status: 'Approved',
      stageCode: 'ST-01',
      stageName: 'مرحلة التجميع',
      workerCode: null,
      workerName: null,
      producedQuantity: 480,
      acceptedQuantity: 450,
      rejectedQuantity: 30,
      workerAllocatedQuantity: null,
      recordCount: 1,
      stageCount: 1,
      workerCount: 4
    }],
    page: 1,
    pageSize: 20,
    totalCount: 1,
    totalPages: 1,
    appliedStatus: 'Approved',
    view: 'Details',
    sortBy: 'ProductionDate',
    sortDirection: 'Ascending'
  };
  const financialResult: FinancialReportResult = {
    ...result,
    summary: {
      totalPhysicalProducedQuantity: 480,
      totalPhysicalAcceptedQuantity: 450,
      totalPhysicalRejectedQuantity: 30,
      recordCount: 3,
      stageCount: 2,
      workerCount: 4,
      totalProductionEarnings: 250,
      totalStageProductionCost: 250,
      averageProductionEarningPerWorker: 62.5,
      averageCostPerPhysicalUnit: 0.52,
      incompleteFinancialRecordCount: 0,
      financialDataStatus: 'Complete',
      currencyCode: 'EGP'
    },
    rows: result.rows.map(row => ({
      ...row,
      stageProductionCost: 250,
      productionEarning: null,
      compensationMode: 'SharedPercentage',
      financialDataStatus: 'Complete'
    }))
  };

  let reports: jasmine.SpyObj<ProductionQuantitiesReportApiService>;
  let financialReports: jasmine.SpyObj<ProductionFinancialReportApiService>;
  let permissions: jasmine.SpyObj<PermissionService>;
  let state: ReportsWorkspaceStateService;
  let component: ReportsWorkspacePageComponent;

  beforeEach(() => {
    localStorage.removeItem('plp.reports-workspace.filters.v1');
    reports = jasmine.createSpyObj<ProductionQuantitiesReportApiService>('ProductionQuantitiesReportApiService', ['query']);
    reports.query.and.returnValue(of(result));
    financialReports = jasmine.createSpyObj<ProductionFinancialReportApiService>('ProductionFinancialReportApiService', ['query']);
    financialReports.query.and.returnValue(of(financialResult));
    permissions = jasmine.createSpyObj<PermissionService>('PermissionService', ['hasAll']);
    permissions.hasAll.and.callFake(required => required.includes(PERMISSIONS.reports.financialView));
    state = new ReportsWorkspaceStateService();
    component = new ReportsWorkspacePageComponent(
      reports,
      financialReports,
      {
        factories: () => of([]),
        allProductionLines: () => of([]),
        models: () => of([]),
        modelStages: () => of([])
      } as unknown as ManufacturingMasterDataApiService,
      {
        listWorkers: () => of([]),
        listOrders: () => of([])
      } as unknown as ProductionCostRecordingApiService,
      state,
      permissions
    );
  });

  afterEach(() => component.ngOnDestroy());

  it('waits for an explicit apply before requesting the approved quantities-only contract', () => {
    component.ngOnInit();

    expect(reports.query).not.toHaveBeenCalled();
    expect(component.loadState).toBe('idle');
    expect(component.presentationMode).toBe('QuantitiesOnly');
    expect(component.filters.from).toMatch(/^\d{4}-\d{2}-01$/);

    component.applyFilters();

    expect(reports.query).toHaveBeenCalledOnceWith(jasmine.objectContaining({ status: 'Approved', view: 'Details' }));
    expect(component.result?.summary).toEqual(result.summary);
    expect(component.loadState).toBe('loaded');
    expect(component.columns.map(column => column.label)).toEqual(['المرحلة', 'التاريخ', 'الحالة', 'كمية المرحلة', 'المقبول', 'المرفوض', 'العمال']);
    expect(JSON.stringify(component.result)).not.toMatch(/salary|price|cost|earning|entitlement|currency|compensation|fixedamount/i);
  });

  it('does not request a pending Draft filter until apply, then preserves it while changing the view', () => {
    component.ngOnInit();
    component.onFiltersChange({ ...component.filters, factoryId: 'factory-1', workerId: 'worker-1', status: 'Draft', page: 2 });

    expect(reports.query).not.toHaveBeenCalled();
    component.applyFilters();
    expect(reports.query).toHaveBeenCalledWith(jasmine.objectContaining({ status: 'Draft', factoryId: 'factory-1', workerId: 'worker-1', page: 1 }));

    component.changeView('StageWorkers');

    expect(component.filters).toEqual(jasmine.objectContaining({ factoryId: 'factory-1', workerId: 'worker-1', status: 'Draft', view: 'StageWorkers', page: 1 }));
    expect(reports.query).toHaveBeenCalledWith(jasmine.objectContaining({ view: 'StageWorkers', status: 'Draft', page: 1, factoryId: 'factory-1', workerId: 'worker-1' }));
    expect(component.columns.map(column => column.label)).toEqual(['المرحلة', 'العامل', 'التاريخ', 'حصة العامل', 'كمية المرحلة']);
  });

  it('persists only applied filters and clears the local report context on reset', () => {
    component.ngOnInit();
    component.onFiltersChange({ ...component.filters, factoryId: 'factory-1', productionLineId: 'line-1' });
    component.applyFilters();

    expect(localStorage.getItem('plp.reports-workspace.filters.v1')).toContain('factory-1');

    component.resetFilters();

    expect(localStorage.getItem('plp.reports-workspace.filters.v1')).toBeNull();
    expect(component.filters).toEqual(jasmine.objectContaining({ factoryId: '', productionLineId: '', status: 'Approved', view: 'Details', page: 1 }));
    expect(component.loadState).toBe('idle');
    expect(reports.query).toHaveBeenCalledTimes(1);
  });

  it('switches to the financial endpoint without losing applied filters, sorting, or pagination', () => {
    component.ngOnInit();
    component.onFiltersChange({ ...component.filters, factoryId: 'factory-1', workerId: 'worker-1' });
    component.applyFilters();
    component.onLazyLoad({ rows: 20, first: 20, sortField: 'StageCode', sortOrder: 1 });

    component.changePresentationMode('QuantitiesAndFinancials');

    expect(component.presentationMode).toBe('QuantitiesAndFinancials');
    expect(financialReports.query).toHaveBeenCalledOnceWith(jasmine.objectContaining({
      factoryId: 'factory-1', workerId: 'worker-1', page: 2, pageSize: 20, sortBy: 'StageCode', sortDirection: 'Ascending'
    }));
    expect(localStorage.getItem('plp.reports-workspace.filters.v1')).toContain('QuantitiesAndFinancials');
    expect(component.result?.summary.totalPhysicalProducedQuantity).toBe(480);
  });

  it('keeps financial mode disabled and never requests the financial API without permission', () => {
    permissions.hasAll.and.returnValue(false);
    localStorage.setItem('plp.reports-workspace.filters.v1', JSON.stringify({ presentationMode: 'QuantitiesAndFinancials' }));

    component.ngOnInit();
    component.changePresentationMode('QuantitiesAndFinancials');

    expect(component.canUseFinancialMode).toBeFalse();
    expect(component.presentationMode).toBe('QuantitiesOnly');
    expect(financialReports.query).not.toHaveBeenCalled();
    expect(component.modeDescription).toContain('صلاحية عرض القيم المالية');
  });

  it('falls back to quantities with a clear message when financial access is rejected', () => {
    financialReports.query.and.returnValue(throwError(() => new HttpErrorResponse({ status: 403 })));
    component.ngOnInit();
    component.applyFilters();
    reports.query.calls.reset();

    component.changePresentationMode('QuantitiesAndFinancials');

    expect(financialReports.query).toHaveBeenCalled();
    expect(component.presentationMode).toBe('QuantitiesOnly');
    expect(component.modeMessage).toContain('تم الرجوع إلى الكميات فقط');
    expect(reports.query).toHaveBeenCalledOnceWith(jasmine.objectContaining({ status: 'Approved', view: 'Details' }));
  });

  it('uses the quantity projection values without treating worker allocations as stage production', () => {
    component.ngOnInit();
    component.applyFilters();
    component.changeView('ByWorker');
    component.result = {
      ...result,
      rows: [{
        ...result.rows[0],
        workerCode: 'W-01',
        workerName: 'عامل التشغيل',
        producedQuantity: null,
        workerAllocatedQuantity: 160,
        stageCount: 2
      }]
    };

    expect(component.rowValue(component.rows[0], 'allocated')).toBe('١٦٠');
    expect(component.rowValue(component.rows[0], 'produced')).toBe('—');
    expect(component.rowValue(component.rows[0], 'worker')).toContain('عامل التشغيل');
    expect(component.columns.map(column => column.label)).toContain('حصة العامل');
  });

  it('keeps the physical operation summary at 500 across views while worker participation remains separately labelled', () => {
    const dailyResult: QuantitiesReportResult = {
      ...result,
      summary: {
        ...result.summary,
        totalPhysicalProducedQuantity: 500,
        totalPhysicalAcceptedQuantity: 500,
        totalPhysicalRejectedQuantity: 0,
        totalStageProducedQuantity: 1500,
        totalAcceptedQuantity: 1500,
        totalRejectedQuantity: 0
      },
      rows: [{
        ...result.rows[0],
        source: { sourceType: 'Worker', workerId: 'worker-1' },
        workerCode: 'W-01', workerName: 'عامل التشغيل', producedQuantity: null,
        acceptedQuantity: null, rejectedQuantity: null, workerAllocatedQuantity: 1250,
        recordCount: 3, stageCount: 3, workerCount: 1
      }]
    };
    reports.query.and.callFake(filter => of({ ...dailyResult, view: filter.view }));
    component.ngOnInit();
    component.applyFilters();

    for (const view of ['Details', 'ByStage', 'ByWorker', 'WorkerStages', 'StageWorkers'] as const) {
      component.changeView(view);
      expect(component.result?.summary.totalPhysicalProducedQuantity).toBe(500);
    }

    expect(component.rows[0].workerAllocatedQuantity).toBe(1250);
    expect(component.rows[0].producedQuantity).toBeNull();
    expect(component.columns.map(column => column.label)).toContain('حصة العامل');
    expect(component.columns.map(column => column.label)).not.toContain('إجمالي الإنتاج');
  });

  it('renders an explicit empty result state after a successful empty response', () => {
    reports.query.and.returnValue(of({ ...result, rows: [], totalCount: 0, totalPages: 0 }));

    component.ngOnInit();
    component.applyFilters();

    expect(component.loadState).toBe('empty');
    expect(component.result?.rows).toEqual([]);
  });

  it('keeps an authorization failure distinct from an empty report', () => {
    reports.query.and.returnValue(throwError(() => new HttpErrorResponse({ status: 403 })));

    component.ngOnInit();
    component.applyFilters();

    expect(component.loadState).toBe('unauthorized');
    expect(component.errorTitle).toBe('لا تملك صلاحية التقرير');
    expect(component.result).toBeNull();
  });
});
