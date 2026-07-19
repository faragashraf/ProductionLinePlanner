import { expect, Page, test } from '@playwright/test';
import { mkdir } from 'node:fs/promises';
import path from 'node:path';

const visualOutput = path.join(process.cwd(), 'test-results', 'reports-workspace');
const quantitiesPermissions = ['reports.production.view', 'production.view', 'factory-structure.view', 'models.view', 'workers.view'];

type ReportScenario = 'data' | 'empty' | 'forbidden' | 'financial-forbidden';

const report = {
  summary: {
    totalPhysicalProducedQuantity: 500,
    totalPhysicalAcceptedQuantity: 500,
    totalPhysicalRejectedQuantity: 0,
    totalStageProducedQuantity: 1500,
    totalAcceptedQuantity: 1500,
    totalRejectedQuantity: 0,
    recordCount: 3,
    stageCount: 3,
    workerCount: 3
  },
  rows: [
    {
      source: { sourceType: 'StageProductionRecord', stageProductionRecordId: 'record-1', productModelStageId: 'stage-1' },
      productionDate: '2026-07-18', status: 'Approved', productionOrderNumber: 'PO-2201',
      factoryCode: 'F-01', factoryName: 'مصنع الاختبار', productionLineCode: 'L-01', productionLineName: 'خط التجميع',
      productModelCode: 'M-01', productModelName: 'موديل اختبار', mainStageName: 'التجميع', stageCode: 'ST-01', stageName: 'مرحلة التجميع',
      workerCode: null, workerName: null, producedQuantity: 500, acceptedQuantity: 480, rejectedQuantity: 20,
      workerAllocatedQuantity: null, recordCount: 1, stageCount: 1, workerCount: 3
    },
    {
      source: { sourceType: 'StageProductionRecord', stageProductionRecordId: 'record-2', productModelStageId: 'stage-2' },
      productionDate: '2026-07-18', status: 'Approved', productionOrderNumber: 'PO-2201',
      factoryCode: 'F-01', factoryName: 'مصنع الاختبار', productionLineCode: 'L-01', productionLineName: 'خط التجميع',
      productModelCode: 'M-01', productModelName: 'موديل اختبار', mainStageName: 'التجميع', stageCode: 'ST-02', stageName: 'مرحلة الفحص والتعبئة',
      workerCode: null, workerName: null, producedQuantity: 500, acceptedQuantity: 500, rejectedQuantity: 0,
      workerAllocatedQuantity: null, recordCount: 1, stageCount: 1, workerCount: 3
    },
    {
      source: { sourceType: 'StageProductionRecord', stageProductionRecordId: 'record-3', productModelStageId: 'stage-3' },
      productionDate: '2026-07-18', status: 'Approved', productionOrderNumber: 'PO-2201',
      factoryCode: 'F-01', factoryName: 'مصنع الاختبار', productionLineCode: 'L-01', productionLineName: 'خط التجميع',
      productModelCode: 'M-01', productModelName: 'موديل اختبار', mainStageName: 'التجميع', stageCode: 'ST-03', stageName: 'مرحلة التغليف',
      workerCode: null, workerName: null, producedQuantity: 500, acceptedQuantity: 500, rejectedQuantity: 0,
      workerAllocatedQuantity: null, recordCount: 1, stageCount: 1, workerCount: 3
    }
  ],
  page: 1, pageSize: 20, totalCount: 2, totalPages: 1,
  appliedStatus: 'Approved', view: 'Details', sortBy: 'ProductionDate', sortDirection: 'Ascending'
};

function reportForView(view: string | null): typeof report {
  if (view === 'ByWorker') {
    return {
      ...report,
      view: 'ByWorker',
      rows: [{
        ...report.rows[0],
        source: { sourceType: 'Worker', workerId: 'worker-1' },
        productionDate: null, productionOrderNumber: null, factoryCode: null, factoryName: null,
        productionLineCode: null, productionLineName: null, productModelCode: null, productModelName: null,
        mainStageName: null, stageCode: null, stageName: null,
        workerCode: 'W-01', workerName: 'عامل التشغيل', producedQuantity: null,
        acceptedQuantity: null, rejectedQuantity: null, workerAllocatedQuantity: 1250,
        recordCount: 3, stageCount: 3, workerCount: 1
      }]
    };
  }

  if (view === 'WorkerStages' || view === 'StageWorkers') {
    return {
      ...report,
      view: view as typeof report.view,
      rows: report.rows.map((row, index) => ({
        ...row,
        source: {
          sourceType: 'StageProductionWorkerAllocation',
          stageProductionRecordId: row.source.stageProductionRecordId,
          stageProductionWorkerAllocationId: `allocation-${index + 1}`,
          productionOrderId: 'order-1', productModelStageId: row.source.productModelStageId, workerId: 'worker-1'
        },
        workerCode: 'W-01', workerName: 'عامل التشغيل', workerAllocatedQuantity: [500, 500, 250][index]
      }))
    };
  }

  return { ...report, view: view === 'ByStage' ? 'ByStage' : 'Details' };
}

function financialReportForView(view: string | null) {
  const quantityResult = reportForView(view);
  return {
    ...quantityResult,
    summary: {
      totalPhysicalProducedQuantity: 500,
      totalPhysicalAcceptedQuantity: 500,
      totalPhysicalRejectedQuantity: 0,
      recordCount: 3,
      stageCount: 3,
      workerCount: 3,
      totalProductionEarnings: 750,
      totalStageProductionCost: 750,
      averageProductionEarningPerWorker: 250,
      averageCostPerPhysicalUnit: 1.5,
      incompleteFinancialRecordCount: 0,
      financialDataStatus: 'Complete',
      currencyCode: 'EGP'
    },
    rows: quantityResult.rows.map(row => ({
      ...row,
      stageProductionCost: 250,
      productionEarning: row.workerAllocatedQuantity ? 125 : null,
      compensationMode: 'SharedPercentage',
      financialDataStatus: 'Complete'
    }))
  };
}

test.beforeAll(async () => {
  await mkdir(visualOutput, { recursive: true });
});

async function openReports(page: Page, scenario: ReportScenario = 'data', financialAccess = false): Promise<void> {
  const currentPermissions = financialAccess
    ? [...quantitiesPermissions, 'reports.financial.view']
    : quantitiesPermissions;
  await page.addInitScript(({ currentPermissions }) => {
    localStorage.setItem('plp.accessToken', 'reports-visual-qa-token');
    localStorage.setItem('plp.currentUser', JSON.stringify({
      id: 'reports-visual-user', fullName: 'مراجع التقارير', email: 'reports.visual@local.test',
      roles: ['Administrator'], permissions: currentPermissions
    }));
  }, { currentPermissions });

  await page.route('**/api/**', async route => {
    const pathname = new URL(route.request().url()).pathname;
    let data: unknown;
    if (pathname.endsWith('/api/auth/me')) {
      data = { id: 'reports-visual-user', fullName: 'مراجع التقارير', email: 'reports.visual@local.test', roles: ['Administrator'], permissions: currentPermissions };
    } else if (pathname.endsWith('/api/reports/production/quantities')) {
      if (scenario === 'forbidden') {
        await route.fulfill({
          status: 403,
          contentType: 'application/json',
          body: JSON.stringify({ success: false, data: null, error: { message: 'Forbidden' } })
        });
        return;
      }
      const reportResult = reportForView(new URL(route.request().url()).searchParams.get('view'));
      data = scenario === 'empty'
        ? { ...reportResult, rows: [], totalCount: 0, totalPages: 0 }
        : reportResult;
    } else if (pathname.endsWith('/api/reports/production/financials')) {
      if (scenario === 'financial-forbidden') {
        await route.fulfill({ status: 403, contentType: 'application/json', body: JSON.stringify({ success: false, data: null, error: { message: 'Forbidden' } }) });
        return;
      }
      data = financialReportForView(new URL(route.request().url()).searchParams.get('view'));
    } else if (pathname.includes('/api/factories')) {
      data = { items: [{ id: 'factory-1', name: 'مصنع الاختبار', code: 'F-01', isActive: true }] };
    } else if (pathname.includes('/api/production-lines')) {
      data = { items: [{ id: 'line-1', factoryId: 'factory-1', name: 'خط التجميع', lineCode: 'L-01', sequenceOrder: 1, isActive: true }] };
    } else if (pathname.includes('/api/product-models')) {
      data = { items: [{ id: 'model-1', code: 'M-01', name: 'موديل اختبار', isActive: true }] };
    } else if (pathname.includes('/api/production/lookups/workers')) {
      data = {
        items: [
          { id: 'worker-1', employeeCode: 'W-01', fullName: 'عامل الاختبار', isActive: true },
          { id: 'worker-2', employeeCode: 'W-2026-LONG', fullName: 'Alexandria Production Worker With A Long Name', isActive: true }
        ]
      };
    } else if (pathname.includes('/api/production/orders')) {
      data = [{
        id: 'order-1',
        orderNumber: 'DLY-20260718-VERY-LONG-REFERENCE-FOR-PRODUCTION-ORDER-VALIDATION',
        productModelId: 'model-1', productModelCode: 'M-01', productionDate: '2026-07-18', plannedQuantity: 1250, status: 'Active'
      }];
    } else {
      data = { items: [] };
    }

    await route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify({ success: true, data, error: null }) });
  });

  await page.goto('/manufacturing/reports');
  await expect(page.locator('[data-reports-workspace="quantities-only"]')).toBeVisible();
}

async function applyFilters(page: Page): Promise<void> {
  await page.getByRole('button', { name: 'تطبيق الفلاتر' }).click();
}

async function scrollToTop(page: Page): Promise<void> {
  await page.evaluate(() => {
    const main = document.querySelector<HTMLElement>('.plp-app-shell__main');
    main?.scrollTo(0, 0);
    const root = document.documentElement;
    const previousBehavior = root.style.scrollBehavior;
    root.style.scrollBehavior = 'auto';
    window.scrollTo(0, 0);
    root.style.scrollBehavior = previousBehavior;
  });
  await expect.poll(() => page.locator('.plp-app-shell__main').evaluate(element => element.scrollTop)).toBe(0);
}

async function verifyWorkspace(page: Page, expectedColumns: number, fileName: string): Promise<void> {
  await expect(page.getByRole('heading', { name: 'تقارير الإنتاج' })).toBeVisible();
  await expect(page.getByText('الكميات فقط', { exact: true })).toBeVisible();
  await expect(page.getByRole('button', { name: 'الكميات والقيم المالية' })).toBeDisabled();
  await expect(page.locator('plp-statistic-card')).toHaveCount(4);
  await expect(page.locator('plp-statistic-card').nth(0).locator('.plp-statistic-card__value')).toHaveText('٥٠٠');
  await expect(page.locator('plp-statistic-card').nth(1).locator('.plp-statistic-card__value')).toHaveText('٣');
  await expect(page.locator('plp-statistic-card').nth(2).locator('.plp-statistic-card__value')).toHaveText('٣');
  await expect(page.locator('plp-statistic-card').nth(3).locator('.plp-statistic-card__value')).toHaveText('٣');
  await expect(page.getByRole('button', { name: 'Excel' })).toBeDisabled();
  await expect(page.getByRole('button', { name: 'طباعة' })).toBeDisabled();
  await expect(page.locator('.reports-workspace__view-option')).toHaveCount(5);
  await expect(page.locator('.reports-workspace__view-grid')).toHaveCSS('grid-template-columns', new RegExp(`^(?:[^ ]+ ){${expectedColumns - 1}}[^ ]+$`));
  await expect(page.locator('.reports-workspace__table [data-report-source]')).toHaveCount(3);

  const overflow = await page.evaluate(() => ({
    scrollWidth: document.documentElement.scrollWidth,
    clientWidth: document.documentElement.clientWidth
  }));
  expect(overflow.scrollWidth).toBeLessThanOrEqual(overflow.clientWidth + 1);

  await scrollToTop(page);
  await page.screenshot({ path: path.join(visualOutput, fileName), fullPage: true });
  await page.locator('.reports-workspace__results').scrollIntoViewIfNeeded();
  await expect(page.locator('.reports-workspace__results')).toBeVisible();
  await page.screenshot({ path: path.join(visualOutput, fileName.replace('.png', '-results.png')) });
}

test('reports workspace on desktop', async ({ page }) => {
  await page.setViewportSize({ width: 1440, height: 900 });
  await openReports(page);
  await applyFilters(page);
  await verifyWorkspace(page, 5, 'desktop-1440x900.png');
});

test('reports workspace keeps the 500 physical operation total across every view and labels the 1250 worker share correctly', async ({ page }) => {
  await page.setViewportSize({ width: 1440, height: 900 });
  await openReports(page);
  await applyFilters(page);

  const views = [
    { label: 'التفاصيل التشغيلية', rows: 3, column: 'كمية المرحلة' },
    { label: 'حسب المرحلة', rows: 3, column: 'كمية المرحلة' },
    { label: 'حسب العامل', rows: 1, column: 'حصة العامل' },
    { label: 'العامل ← المراحل', rows: 3, column: 'حصة العامل' },
    { label: 'المرحلة ← العمال', rows: 3, column: 'حصة العامل' }
  ];

  for (const view of views) {
    await page.getByRole('button', { name: new RegExp(view.label) }).click();
    await expect(page.locator('plp-statistic-card').first().locator('.plp-statistic-card__value')).toHaveText('٥٠٠');
    await expect(page.locator('.reports-workspace__table [data-report-source]')).toHaveCount(view.rows);
    await expect(page.getByRole('columnheader', { name: view.column, exact: true })).toBeVisible();
    await expect(page.getByRole('columnheader', { name: 'إجمالي الإنتاج', exact: true })).toHaveCount(0);
    if (view.label === 'حسب العامل') {
      await expect(page.locator('.reports-workspace__table')).toContainText('١٬٢٥٠');
    }
  }

  await page.locator('.reports-workspace__results').scrollIntoViewIfNeeded();
  await page.screenshot({ path: path.join(visualOutput, 'physical-total-500-across-views.png') });
});

test('reports workspace on laptop', async ({ page }) => {
  await page.setViewportSize({ width: 1100, height: 800 });
  await openReports(page);
  await applyFilters(page);
  await verifyWorkspace(page, 3, 'laptop-1100x800.png');
});

test('reports workspace on Android tablet landscape', async ({ page }) => {
  await page.setViewportSize({ width: 1280, height: 800 });
  await openReports(page);
  await applyFilters(page);
  await verifyWorkspace(page, 5, 'tablet-landscape-1280x800.png');
});

test('reports workspace on Android tablet portrait', async ({ page }) => {
  await page.setViewportSize({ width: 800, height: 1280 });
  await openReports(page);
  await applyFilters(page);
  await verifyWorkspace(page, 2, 'tablet-portrait-800x1280.png');
});

test('reports workspace selects the authorized financial projection without changing the current result layout', async ({ page }) => {
  await page.setViewportSize({ width: 1440, height: 900 });
  await openReports(page, 'data', true);
  await applyFilters(page);

  const financialRequest = page.waitForRequest(request => new URL(request.url()).pathname.endsWith('/api/reports/production/financials'));
  await page.getByRole('button', { name: 'الكميات والقيم المالية' }).click();
  await financialRequest;

  await expect(page.locator('[data-reports-workspace="quantities-and-financials"]')).toBeVisible();
  await expect(page.getByText('وضع القيم المالية مفعّل ضمن نفس نطاق الفلاتر، ويعرض قيم المراحل وأرباح العمال من اللقطات المحفوظة.')).toBeVisible();
  await expect(page.locator('.reports-workspace__table [data-report-source]')).toHaveCount(3);
  await page.screenshot({ path: path.join(visualOutput, 'financial-desktop-1440x900.png'), fullPage: true });
});

test('reports workspace returns to quantities when the authorized financial request is forbidden', async ({ page }) => {
  await page.setViewportSize({ width: 1440, height: 900 });
  await openReports(page, 'financial-forbidden', true);
  await applyFilters(page);
  await page.getByRole('button', { name: 'الكميات والقيم المالية' }).click();

  await expect(page.locator('[data-reports-workspace="quantities-only"]')).toBeVisible();
  await expect(page.getByText('تم الرجوع إلى الكميات فقط لأن صلاحية عرض القيم المالية غير متاحة.')).toBeVisible();
  await expect(page.locator('.reports-workspace__table [data-report-source]')).toHaveCount(3);
  await page.screenshot({ path: path.join(visualOutput, 'financial-forbidden-desktop-1440x900.png'), fullPage: true });
});

test('reports workspace keeps the presentation selector contained on mobile', async ({ page }) => {
  await page.setViewportSize({ width: 390, height: 844 });
  await openReports(page);
  const selector = page.locator('.reports-workspace__mode-selector');
  await expect(selector).toBeVisible();
  const bounds = await selector.boundingBox();
  expect(bounds).not.toBeNull();
  expect(bounds!.x).toBeGreaterThanOrEqual(0);
  expect(bounds!.x + bounds!.width).toBeLessThanOrEqual(390);
  await page.screenshot({ path: path.join(visualOutput, 'mobile-390x844.png'), fullPage: true });
});

test('reports workspace distinguishes an empty response from an unapplied filter state', async ({ page }) => {
  await page.setViewportSize({ width: 1440, height: 900 });
  await openReports(page, 'empty');
  await expect(page.getByText('ابدأ بتطبيق الفلاتر')).toBeVisible();
  await applyFilters(page);
  await expect(page.getByText('لا توجد سجلات مطابقة للفلاتر الحالية')).toBeVisible();
  await expect(page.locator('.reports-workspace__table')).toHaveCount(0);
  await scrollToTop(page);
  await page.screenshot({ path: path.join(visualOutput, 'empty-1440x900.png'), fullPage: true });
});

test('reports workspace shows an authorization state instead of an empty result', async ({ page }) => {
  await page.setViewportSize({ width: 1440, height: 900 });
  await openReports(page, 'forbidden');
  await applyFilters(page);
  await expect(page.getByText('لا تملك صلاحية التقرير')).toBeVisible();
  await expect(page.getByText('لا توجد سجلات مطابقة للفلاتر الحالية')).toHaveCount(0);
  await scrollToTop(page);
  await page.screenshot({ path: path.join(visualOutput, 'forbidden-1440x900.png'), fullPage: true });
});

test('reports workspace keeps long worker and order values contained in tablet portrait', async ({ page }) => {
  await page.setViewportSize({ width: 800, height: 1280 });
  await openReports(page);
  const orderControl = page.locator('[data-report-filter="production-order"]');
  await orderControl.click();
  await page.getByText('DLY-20260718-VERY-LONG-REFERENCE-FOR-PRODUCTION-ORDER-VALIDATION', { exact: true }).click();
  await expect(orderControl).toHaveAttribute('title', 'DLY-20260718-VERY-LONG-REFERENCE-FOR-PRODUCTION-ORDER-VALIDATION');
  const orderBounds = await orderControl.boundingBox();
  expect(orderBounds).not.toBeNull();
  expect(orderBounds!.x).toBeGreaterThanOrEqual(0);
  expect(orderBounds!.x + orderBounds!.width).toBeLessThanOrEqual(800);
  await expect(page.locator('.p-dropdown-panel:visible')).toHaveCount(0);
  await page.locator('[data-report-filter="worker"]').click();
  await expect(page.getByText('Alexandria Production Worker With A Long Name', { exact: true })).toBeVisible();
  await expect(page.locator('.p-dropdown-panel.plp-reports-filter-overlay:visible')).toHaveCount(1);

  const overflow = await page.evaluate(() => ({ scrollWidth: document.documentElement.scrollWidth, clientWidth: document.documentElement.clientWidth }));
  expect(overflow.scrollWidth).toBeLessThanOrEqual(overflow.clientWidth + 1);
  await page.screenshot({ path: path.join(visualOutput, 'tablet-portrait-worker-dropdown-long-order.png'), fullPage: true });
});
