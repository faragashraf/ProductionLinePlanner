import { expect, Page, test } from '@playwright/test';
import ExcelJS from 'exceljs';
import { mkdir, stat } from 'node:fs/promises';
import path from 'node:path';

const visualOutput = path.join(process.cwd(), 'test-results', 'model-journey-daily-preview');
const excelOutput = path.join(process.cwd(), 'test-results', 'daily-production-excel');
const permissions = ['models.view', 'models.manage', 'production.view', 'production.record', 'assignments.manage', 'attendance.sync'];
const factory = { id: 'factory-1', code: 'F-01', name: 'مصنع الاختبار', isActive: true };
const department = { id: 'department-1', factoryId: factory.id, code: 'CUT', nameAr: 'قسم القص', isActive: true };
const line = { id: 'line-1', factoryId: factory.id, departmentId: department.id, lineCode: 'L-01', name: 'خط التشغيل اليومي', sequenceOrder: 1, isActive: true };
const model = { id: 'model-1', code: 'M-01', name: 'موديل الاختبار', isActive: true };
const stage = { id: 'stage-1', mainStageId: 'main-1', mainStageName: 'التشغيل', factoryId: factory.id, departmentId: department.id, factoryName: factory.name, departmentNameAr: department.nameAr, code: 'ST-01', name: 'مرحلة التجميع', capacity: 5, sequenceOrder: 1, isActive: true };
const secondStage = { ...stage, id: 'stage-2', code: 'ST-02', name: 'مرحلة التشطيب', sequenceOrder: 2 };
const modelStage = { id: 'model-stage-1', productionLineId: line.id, subStageId: stage.id, departmentId: department.id, subStageCode: stage.code, subStageName: stage.name, stageOrder: 1, piecePrice: .6, standardSeconds: 30, compensationMode: 'SharedPercentage', isRequired: true, isActive: true };
const secondModelStage = { ...modelStage, id: 'model-stage-2', subStageId: secondStage.id, subStageCode: secondStage.code, subStageName: secondStage.name, stageOrder: 2, piecePrice: .75 };

test.beforeAll(async () => {
  await mkdir(visualOutput, { recursive: true });
  await mkdir(excelOutput, { recursive: true });
});

async function preparePage(page: Page): Promise<void> {
  await page.addInitScript(({ storedPermissions }) => {
    localStorage.setItem('plp.accessToken', 'visual-token');
    localStorage.setItem('plp.currentUser', JSON.stringify({ id: 'visual-user', fullName: 'مراجع الواجهة', roles: ['Administrator'], permissions: storedPermissions }));
  }, { storedPermissions: permissions });
  await page.route('**/hubs/notifications/negotiate**', route => route.fulfill({
    status: 200,
    contentType: 'application/json',
    body: JSON.stringify({ connectionId: 'visual-connection', connectionToken: 'visual-connection', negotiateVersion: 1, availableTransports: [{ transport: 'WebSockets', transferFormats: ['Text', 'Binary'] }] })
  }));
  await page.routeWebSocket('**/hubs/notifications**', socket => socket.onMessage(message => { if (typeof message === 'string' && message.includes('"protocol"')) socket.send('{}\u001e'); }));
  await page.route('**/api/**', async route => {
    const url = new URL(route.request().url());
    const pathname = url.pathname;
    let data: unknown = { items: [] };
    if (pathname.endsWith('/api/auth/me')) data = { id: 'visual-user', fullName: 'مراجع الواجهة', roles: ['Administrator'], permissions };
    else if (pathname.endsWith('/api/factories')) data = { items: [factory] };
    else if (pathname.endsWith('/api/departments')) data = { items: [department] };
    else if (pathname.endsWith('/api/production-lines')) data = { items: [line] };
    else if (pathname.endsWith(`/api/product-models/${model.id}/production-lines/${line.id}/stages`)) data = [modelStage, secondModelStage];
    else if (pathname.endsWith('/api/product-models')) data = { items: [model], totalCount: 1, pageNumber: 1, pageSize: 10 };
    else if (pathname.endsWith('/api/stages')) data = { items: [stage], totalCount: 1, pageNumber: 1, pageSize: 200 };
    else if (pathname.includes('/api/attendance/sync/production-date/')) data = { sourceUsersCount: 1, sourceCheckInsCount: 1, matchedWorkersCount: 1, unmatchedSourceUsersCount: 0, workersWithoutAttendanceCount: 0, insertedRecords: 1, updatedRecords: 0, skippedRecords: 0 };
    else if (pathname.endsWith('/api/production/daily-operations/preview')) data = dailyPreview();
    else if (pathname.endsWith('/api/production/daily-operations')) data = dailyOperations();
    await route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify({ success: true, data, error: null }) });
  });
}

function dailyWorker() {
  return { workerId: 'worker-1', workerCode: 'W-01', workerName: 'عامل الاختبار', isOnActiveService: true, effectiveAssignmentType: 'Permanent', attendanceStatus: 'Present', hasSourceCheckIn: true, isPresent: true, requiresAuthorizedOverride: false, suggestedPercentage: 25, contributionStartsAtUtc: '2026-07-17T05:00:00Z', contributionEndsAtUtc: '2026-07-17T13:00:00Z', workerMinutes: 480, isProductionReady: true, isAssignedWorker: true, isDailyOverride: false };
}

function secondDailyWorker() {
  return { ...dailyWorker(), workerId: 'worker-2', workerCode: 'W-02', workerName: 'عامل مساعد', suggestedPercentage: 75 };
}

function dailyOperations() {
  const firstOperationStage = { productModelStageId: modelStage.id, subStageId: stage.id, mainStageName: 'التجميع', stageCode: stage.code, stageName: stage.name, stageOrder: 1, piecePrice: .6, compensationMode: 'SharedPercentage', staffingStatus: 'Staffed', attendanceStatus: 'Ready', hasAbsentWorkers: false, hasNoSourceCheckInWorkers: false, isFinancialReviewPending: false, isReady: true, workers: [dailyWorker(), secondDailyWorker()] };
  return { factoryId: factory.id, factoryName: factory.name, productionLineId: line.id, productionLineName: line.name, productModelId: model.id, productModelCode: model.code, productModelName: model.name, productionDate: '2026-07-17', staffingContextVersion: 'visual-v1', totalStages: 2, readyStages: 2, stagesWithAbsentWorkers: 0, stagesWithNoSourceCheckIn: 0, stagesWithoutStaffing: 0, stagesRequiringCostReview: 0, activeWorkers: [dailyWorker(), secondDailyWorker()], stages: [firstOperationStage, { ...firstOperationStage, productModelStageId: secondModelStage.id, subStageId: secondStage.id, stageCode: secondStage.code, stageName: secondStage.name, stageOrder: 2, piecePrice: .75, workers: [{ ...dailyWorker(), suggestedPercentage: 100 }] }] };
}

function dailyPreview() {
  const firstWarning = 'تحتاج مرحلة التجميع إلى مراجعة توزيع العمال قبل حفظ المسودة لضمان اكتمال بيانات التشغيل.';
  const secondWarning = 'تحتاج مرحلة التشطيب إلى مراجعة مدير الإنتاج قبل اعتماد الكمية النهائية.';
  return { productionDate: '2026-07-17', lineQuantity: 1000, previewToken: 'visual-preview', totalWorkerEntitlements: 1350, stages: [{ productModelStageId: modelStage.id, stageCode: stage.code, stageName: stage.name, stageQuantity: 1000, stageCost: 600, compensationMode: 'SharedPercentage', warnings: [firstWarning], workers: [{ workerId: 'worker-1', workerCode: 'W-01', workerName: 'عامل الاختبار', percentage: 25, equivalentQuantity: 250, calculatedEarning: 150 }, { workerId: 'worker-2', workerCode: 'W-02', workerName: 'عامل مساعد', percentage: 75, equivalentQuantity: 750, calculatedEarning: 450 }] }, { productModelStageId: secondModelStage.id, stageCode: secondStage.code, stageName: secondStage.name, stageQuantity: 1000, stageCost: 750, compensationMode: 'SharedPercentage', warnings: [secondWarning], workers: [{ workerId: 'worker-1', workerCode: 'W-01', workerName: 'عامل الاختبار', percentage: 100, equivalentQuantity: 1000, calculatedEarning: 750 }] }], workerTotals: [{ workerId: 'worker-1', workerCode: 'W-01', workerName: 'عامل الاختبار', totalEntitlement: 900 }, { workerId: 'worker-2', workerCode: 'W-02', workerName: 'عامل مساعد', totalEntitlement: 450 }], warnings: [firstWarning, secondWarning, 'تحذير عام لا يرتبط بمرحلة محددة.'] };
}

async function openSuccessfulDailyPreview(page: Page): Promise<void> {
  await page.goto('/manufacturing/daily-production-operations');
  await page.getByLabel('تاريخ الإنتاج').fill('2026-07-17');
  await page.locator('.structure-selector__trigger').click();
  await page.getByPlaceholder('ابحث بالاسم أو الكود').fill(line.lineCode);
  const lineNode = page.locator('.p-treenode-content').filter({
    has: page.locator('.factory-structure-node--line[data-selectable="true"]')
  });
  await expect(lineNode).toBeVisible();
  await lineNode.evaluate(element => (element as HTMLElement).click());
  await expect(page.locator('.structure-selector__trigger')).toContainText(line.name);
  await page.getByLabel('الموديل').selectOption(model.id);
  await page.getByRole('button', { name: 'مزامنة حضور التاريخ' }).click();
  await page.getByRole('button', { name: 'تحميل تشغيل اليوم' }).click();
  await page.getByLabel('كمية تشغيل الخط (تطبق مرة واحدة على كل مرحلة)').fill('1000');
  await page.getByRole('button', { name: 'حساب معاينة موحّدة' }).click();
  await expect(page.locator('.daily-production-operations__preview-overview')).toBeVisible();
  await expect(page.getByRole('button', { name: 'تصدير Excel' })).toBeVisible();
}

async function expectViewportSafe(page: Page): Promise<void> {
  const geometry = await page.evaluate(() => ({ direction: getComputedStyle(document.documentElement).direction, scrollWidth: document.documentElement.scrollWidth, clientWidth: document.documentElement.clientWidth }));
  expect(geometry.direction).toBe('rtl');
  expect(geometry.scrollWidth).toBeLessThanOrEqual(geometry.clientWidth + 1);
}

test('line-scoped model journey search stays inside the assignment table', async ({ page }) => {
  await preparePage(page);
  for (const [name, width, height] of [['desktop', 1440, 900], ['tablet-landscape', 1280, 800], ['tablet-portrait', 800, 1280], ['mobile', 390, 844]] as const) {
    await page.setViewportSize({ width, height });
    await page.goto('/manufacturing/models');
    const contextTree = page.locator('.master-page__model-context');
    await contextTree.locator('.p-tree-toggler').first().click();
    const modelNode = contextTree.locator('.p-treenode-content', { hasText: `${model.code} — ${model.name}` });
    await modelNode.locator('.p-tree-toggler').click();
    const departmentNode = contextTree.locator('.p-treenode-content', { hasText: department.nameAr });
    await departmentNode.locator('.p-tree-toggler').click();
    await contextTree.locator('.p-treenode-content', { hasText: line.name }).click();
    const stageSearch = page.getByPlaceholder('ابحث باسم أو كود المرحلة');
    await expect(stageSearch).toBeVisible();
    await stageSearch.fill('ST-01');
    await expect(page.locator('tbody')).toContainText('مرحلة التجميع');
    await expect(page.locator('tbody')).not.toContainText('مرحلة التشطيب');
    const activationToggle = page.getByRole('button', { name: 'تعطيل الارتباط بالموديل' });
    await expect(activationToggle).toBeVisible();
    const toggleBox = await activationToggle.boundingBox();
    expect(toggleBox?.height).toBeGreaterThanOrEqual(44);
    await activationToggle.screenshot({ path: path.join(visualOutput, `model-stage-toggle-${name}.png`) });
    await expect(contextTree.getByRole('treeitem', { name: `${line.lineCode} — ${line.name}` })).toHaveAttribute('aria-selected', 'true');
    await page.screenshot({ path: path.join(visualOutput, `models-${name}.png`), fullPage: true });
    await expectViewportSafe(page);
  }
});

test('daily filters wrap safely and successful unified preview renders summary plus Excel action without legacy tables', async ({ page }) => {
  await preparePage(page);
  for (const [name, width, height] of [['desktop', 1440, 900], ['tablet-landscape', 1280, 800], ['tablet-portrait', 800, 1280], ['mobile', 390, 844]] as const) {
    await page.setViewportSize({ width, height });
    await openSuccessfulDailyPreview(page);
    await expect(page.locator('.daily-production-operations__legacy-table')).toHaveCount(0);
    await expect(page.getByText('فلتر عرض فقط؛ لا يغيّر المسودة أو نتيجة المعاينة.')).toBeVisible();
    const blockerButtons = page.locator('button.daily-production-operations__preview-blocker');
    await expect(blockerButtons).toHaveCount(2);
    await expect(page.locator('.daily-production-operations__preview-blocker--global')).toHaveCount(1);
    const firstBlockerBox = await blockerButtons.first().boundingBox();
    expect(firstBlockerBox?.height).toBeGreaterThanOrEqual(44);
    await page.locator('.daily-production-operations__preview-blockers').screenshot({ path: path.join(visualOutput, `daily-blockers-${name}.png`) });
    await blockerButtons.first().focus();
    await expect(blockerButtons.first()).toBeFocused();
    await blockerButtons.first().click();
    await expect(page.locator('.daily-production-operations__stage-list > button')).toHaveCount(1);
    await expect(page.locator(`#daily-stage-row-${modelStage.id}`)).toBeInViewport();
    await blockerButtons.nth(1).click();
    await expect(page.locator('.daily-production-operations__stage-list > button')).toHaveCount(1);
    await expect(page.locator(`#daily-stage-row-${secondModelStage.id}`)).toBeInViewport();
    await expect(page.locator('.daily-production-operations__active-stage-filter')).toContainText(secondStage.name);
    await page.getByRole('button', { name: 'عرض كل المراحل' }).click();
    await expect(page.locator('.daily-production-operations__stage-list > button')).toHaveCount(2);
    await expect(page.locator('.daily-production-operations__active-stage-filter')).toHaveCount(0);
    await page.locator('.daily-production-operations__stage-filter .p-dropdown-trigger').click();
    await page.getByRole('option', { name: secondStage.name, exact: true }).click();
    await expect(page.locator('.daily-production-operations__stage-filter-panel')).toBeHidden();
    const selectedStageRow = page.locator(`#daily-stage-row-${secondModelStage.id}`);
    await expect(page.locator('.daily-production-operations__stage-list > button')).toHaveCount(1);
    await expect(selectedStageRow).toHaveAttribute('aria-current', 'true');
    await expect(selectedStageRow).toHaveClass(/is-selected/);
    await expect(page.locator('.daily-production-operations__detail-panel')).toContainText(secondStage.name);
    await page.screenshot({ path: path.join(visualOutput, `daily-${name}.png`), fullPage: true });
    const clearStageFilter = page.locator('.daily-production-operations__stage-filter .p-dropdown-clear-icon');
    await expect(clearStageFilter).toHaveCount(1);
    await clearStageFilter.click();
    await expect(page.locator('.daily-production-operations__stage-list > button')).toHaveCount(2);
    await expect(selectedStageRow).toHaveAttribute('aria-current', 'true');
    await expectViewportSafe(page);

    await page.getByPlaceholder('ابحث باسم أو كود المرحلة').fill(stage.code);
    await page.locator('.daily-production-operations__stage-filter .p-dropdown-trigger').click();
    await page.getByRole('option', { name: secondStage.name, exact: true }).click();
    await expect(page.locator('.daily-production-operations__stage-filter-panel')).toBeHidden();
    await expect(page.locator('.daily-production-operations__stage-list > button')).toHaveCount(0);
    await expect(page.getByText('لا توجد مراحل مطابقة لفلتر المرحلة الحالي.')).toBeVisible();
    await page.screenshot({ path: path.join(visualOutput, `daily-empty-${name}.png`), fullPage: true });
    await page.locator('.daily-production-operations__stage-filter .p-dropdown-clear-icon').click();
    await expect(page.locator('.daily-production-operations__stage-list > button')).toHaveCount(1);
    await expect(page.locator('.daily-production-operations__stage-list > button.is-selected')).toHaveCount(0);
    await expect(page.getByText('لا توجد مراحل مطابقة لفلتر المرحلة الحالي.')).toHaveCount(0);
    await expectViewportSafe(page);
  }
});

test('downloads and opens a real multi-sheet Excel workbook from the full preview', async ({ page }) => {
  const consoleErrors: string[] = [];
  page.on('console', message => { if (message.type() === 'error') consoleErrors.push(message.text()); });
  page.on('pageerror', error => consoleErrors.push(error.stack ?? error.message));
  await preparePage(page);
  await openSuccessfulDailyPreview(page);

  await page.locator('.daily-production-operations__stage-filter .p-dropdown-trigger').click();
  await page.getByRole('option', { name: stage.name, exact: true }).click();
  consoleErrors.length = 0;

  const exportButton = page.getByRole('button', { name: 'تصدير Excel' });
  const [download] = await Promise.all([
    page.waitForEvent('download'),
    exportButton.click()
  ]);
  const suggestedFileName = download.suggestedFilename();
  const savedPath = path.join(excelOutput, 'daily-production-runtime.xlsx');
  await download.saveAs(savedPath);
  const fileStats = await stat(savedPath);

  expect(suggestedFileName).toBe('Production-Daily_2026-07-17_L-01_M-01_غير-محفوظة.xlsx');
  expect(fileStats.size).toBeGreaterThan(0);
  const workbook = new ExcelJS.Workbook();
  await workbook.xlsx.readFile(savedPath);
  expect(workbook.worksheets.map(sheet => sheet.name)).toEqual(['تفاصيل الإنتاج', 'ملخص المراحل', 'ملخص العمال', 'بيانات التشغيل']);
  const detailSheet = workbook.getWorksheet('تفاصيل الإنتاج')!;
  const detailHeaders = detailSheet.getRow(1).values as unknown[];
  const column = (header: string) => detailHeaders.findIndex(value => value === header);
  expect(detailSheet.getCell(2, column('كمية العامل في المرحلة')).value).toBe(250);
  expect(detailSheet.getCell(2, column('سعر القطعة')).value).toBe(.6);
  expect(detailSheet.getCell(2, column('قيمة إنتاج العامل')).value).toBe(150);
  expect(detailSheet.getCell(2, column('إجمالي الزمن القياسي للعامل بالثواني')).value).toBe(7500);
  expect(detailSheet.getCell(2, column('إجمالي الزمن بالدقائق')).value).toBe(125);
  expect(detailSheet.getCell(3, column('كمية العامل في المرحلة')).value).toBe(750);
  expect(detailSheet.getCell(3, column('قيمة إنتاج العامل')).value).toBe(450);
  expect(detailSheet.getCell(3, column('إجمالي الزمن القياسي للعامل بالثواني')).value).toBe(22500);
  expect(detailSheet.getCell(3, column('إجمالي الزمن بالدقائق')).value).toBe(375);
  const stageSheet = workbook.getWorksheet('ملخص المراحل')!;
  const stageHeaders = stageSheet.getRow(1).values as unknown[];
  const stageColumn = (header: string) => stageHeaders.findIndex(value => value === header);
  expect(stageSheet.getCell(2, stageColumn('إجمالي قيمة المرحلة')).value).toBe(600);
  expect(stageSheet.getCell(2, stageColumn('إجمالي الزمن')).value).toBe(30000);
  expect(detailSheet.views[0]?.rightToLeft).toBe(true);
  expect(detailSheet.autoFilter).toBeTruthy();
  await expect(page.locator('.p-toast-detail').filter({ hasText: 'تم إنشاء ملف Excel بنجاح.' })).toBeVisible();
  expect(consoleErrors).toEqual([]);
});
