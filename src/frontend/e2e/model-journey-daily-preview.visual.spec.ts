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
const validationStageDefinitions = [
  ['STG009', 'مرحلة تجهيز الحافة الطويلة للاختبار', '1505'],
  ['STG011', 'مرحلة تركيب الجزء الداخلي', '1242'],
  ['STG013', 'مرحلة ضبط المقاس النهائي', '2309'],
  ['STG020', 'مرحلة المراجعة الدقيقة قبل التشطيب', '1710'],
  ['STG028', 'مرحلة التشطيب والتجهيز للتسليم', '1988'],
  ['STG031', 'مرحلة الفحص النهائي واعتماد الجودة', '2114']
] as const;
const validationModelStages = validationStageDefinitions.map(([code, name], index) => ({
  ...modelStage,
  id: `validation-model-stage-${index + 1}`,
  subStageId: `validation-sub-stage-${index + 1}`,
  subStageCode: code,
  subStageName: name,
  stageOrder: index + 1
}));

test.beforeAll(async () => {
  await mkdir(visualOutput, { recursive: true });
  await mkdir(excelOutput, { recursive: true });
});

async function preparePage(page: Page, options: {
  validationBlockers?: boolean;
  historicalDraft?: boolean;
  onPreview?: (payload: any) => void;
  onDraftUpdate?: (payload: any) => void;
} = {}): Promise<void> {
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
    else if (pathname.endsWith(`/api/product-models/${model.id}/production-lines/${line.id}/stages`)) data = options.validationBlockers
      ? validationModelStages
      : [modelStage, secondModelStage];
    else if (pathname.endsWith('/api/product-models')) data = { items: [model], totalCount: 1, pageNumber: 1, pageSize: 10 };
    else if (pathname.endsWith('/api/stages')) data = { items: [stage], totalCount: 1, pageNumber: 1, pageSize: 200 };
    else if (pathname.includes('/api/attendance/sync/production-date/')) data = { sourceUsersCount: 1, sourceCheckInsCount: 1, matchedWorkersCount: 1, unmatchedSourceUsersCount: 0, workersWithoutAttendanceCount: 0, insertedRecords: 1, updatedRecords: 0, skippedRecords: 0 };
    else if (pathname.endsWith('/api/production/daily-operations/preview')) {
      options.onPreview?.(route.request().postDataJSON());
      data = options.historicalDraft ? historicalDraftPreview() : dailyPreview();
    }
    else if (pathname.endsWith('/api/production/daily-operations/drafts/historical-order')) {
      const payload = route.request().postDataJSON();
      options.onDraftUpdate?.(payload);
      data = historicalDraft();
    }
    else if (pathname.endsWith('/api/production/daily-operations')) data = options.validationBlockers
      ? validationDailyOperations()
      : options.historicalDraft ? historicalDraftDailyOperations() : dailyOperations();
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

function validationDailyOperations() {
  const stages = validationModelStages.map((validationStage, index) => {
    const [, , workerCode] = validationStageDefinitions[index];
    return {
      productModelStageId: validationStage.id,
      subStageId: validationStage.subStageId,
      mainStageName: 'التشغيل',
      stageCode: validationStage.subStageCode,
      stageName: validationStage.subStageName,
      stageOrder: validationStage.stageOrder,
      piecePrice: .6 + index * .05,
      compensationMode: 'SharedPercentage',
      staffingStatus: 'Staffed',
      attendanceStatus: 'Ready',
      hasAbsentWorkers: false,
      hasNoSourceCheckInWorkers: false,
      isFinancialReviewPending: false,
      isReady: true,
      workers: [{
        ...dailyWorker(),
        workerId: `validation-worker-${index + 1}`,
        workerCode,
        workerName: `عامل مرحلة ${validationStage.stageOrder}`,
        suggestedPercentage: 100,
        isAssignedWorker: false,
        isDailyOverride: true
      }]
    };
  });
  return {
    factoryId: factory.id,
    factoryName: factory.name,
    productionLineId: line.id,
    productionLineName: line.name,
    productModelId: model.id,
    productModelCode: model.code,
    productModelName: model.name,
    productionDate: '2026-07-17',
    staffingContextVersion: 'validation-v1',
    totalStages: stages.length,
    readyStages: stages.length,
    stagesWithAbsentWorkers: 0,
    stagesWithNoSourceCheckIn: 0,
    stagesWithoutStaffing: 0,
    stagesRequiringCostReview: 0,
    activeWorkers: stages.flatMap(stage => stage.workers),
    stages
  };
}

function historicalDraft() {
  const savedWorker = (workerId: string, workerCode: string, workerName: string) => ({
    workerId,
    workerCode,
    workerName,
    percentage: 100,
    inputQuantity: 1000,
    equivalentQuantity: 1000,
    calculatedEarning: 600
  });
  const savedStage = (id: string, productModelStageId: string, stageCode: string, stageName: string, worker: any) => ({
    id,
    productionOrderId: 'historical-order',
    productModelStageId,
    productionDate: '2026-07-17',
    producedQuantity: 1000,
    acceptedQuantity: 1000,
    rejectedQuantity: 0,
    status: 'Draft',
    stageCode,
    stageName,
    productModelCode: model.code,
    productModelName: model.name,
    factoryCode: factory.code,
    factoryName: factory.name,
    productionLineCode: line.lineCode,
    productionLineName: line.name,
    mainStageName: 'التشغيل',
    piecePrice: .6,
    standardSeconds: 30,
    compensationMode: 'SharedPercentage',
    totalWorkerEarnings: 600,
    concurrencyToken: `token-${id}`,
    workers: [worker]
  });
  return {
    productionOrderId: 'historical-order',
    orderNumber: 'DLY-HISTORICAL',
    orderStatus: 'Draft',
    concurrencyToken: 'historical-order-token',
    productionDate: '2026-07-17',
    recordedAtUtc: '2026-07-17T13:00:00Z',
    lineQuantity: 1000,
    wasAlreadySaved: false,
    stages: [
      savedStage('record-active', modelStage.id, stage.code, stage.name, savedWorker('worker-active', 'W-ACTIVE', 'عامل المرحلة النشطة')),
      savedStage('record-003', 'historical-stage-003', 'STG003', 'مرحلة تاريخية 003', savedWorker('worker-003', 'W003', 'عامل تاريخي 003')),
      savedStage('record-060', 'historical-stage-060', 'STG060', 'مرحلة تاريخية 060', savedWorker('worker-060', 'W060', 'عامل تاريخي 060'))
    ]
  };
}

function historicalDraftDailyOperations() {
  const activeWorker = {
    ...dailyWorker(),
    workerId: 'worker-active',
    workerCode: 'W-ACTIVE',
    workerName: 'عامل المرحلة النشطة',
    suggestedPercentage: 100,
    isAssignedWorker: false,
    isDailyOverride: true
  };
  const activeStage = {
    productModelStageId: modelStage.id,
    subStageId: stage.id,
    mainStageName: 'التشغيل',
    stageCode: stage.code,
    stageName: stage.name,
    stageOrder: 1,
    piecePrice: .6,
    compensationMode: 'SharedPercentage',
    staffingStatus: 'Staffed',
    attendanceStatus: 'Ready',
    hasAbsentWorkers: false,
    hasNoSourceCheckInWorkers: false,
    isFinancialReviewPending: false,
    isReady: true,
    workers: [activeWorker]
  };
  const currentOnlyWorker = {
    ...dailyWorker(),
    workerId: 'worker-current-only',
    workerCode: 'W-CURRENT',
    workerName: 'عامل المرحلة الجديدة',
    suggestedPercentage: 100
  };
  const currentOnlyStage = {
    ...activeStage,
    productModelStageId: secondModelStage.id,
    subStageId: secondStage.id,
    stageCode: secondStage.code,
    stageName: secondStage.name,
    stageOrder: 2,
    piecePrice: secondModelStage.piecePrice,
    workers: [currentOnlyWorker]
  };
  return {
    factoryId: factory.id,
    factoryName: factory.name,
    productionLineId: line.id,
    productionLineName: line.name,
    productModelId: model.id,
    productModelCode: model.code,
    productModelName: model.name,
    productionDate: '2026-07-17',
    staffingContextVersion: 'historical-v1',
    totalStages: 2,
    readyStages: 2,
    stagesWithAbsentWorkers: 0,
    stagesWithNoSourceCheckIn: 0,
    stagesWithoutStaffing: 0,
    stagesRequiringCostReview: 0,
    activeWorkers: [activeWorker, currentOnlyWorker],
    stages: [activeStage, currentOnlyStage],
    existingDraft: historicalDraft()
  };
}

function dailyPreview() {
  const firstWarning = 'تحتاج مرحلة التجميع إلى مراجعة توزيع العمال قبل حفظ المسودة لضمان اكتمال بيانات التشغيل.';
  const secondWarning = 'تحتاج مرحلة التشطيب إلى مراجعة مدير الإنتاج قبل اعتماد الكمية النهائية.';
  return { productionDate: '2026-07-17', lineQuantity: 1000, previewToken: 'visual-preview', totalWorkerEntitlements: 1350, stages: [{ productModelStageId: modelStage.id, stageCode: stage.code, stageName: stage.name, stageQuantity: 1000, stageCost: 600, compensationMode: 'SharedPercentage', warnings: [firstWarning], workers: [{ workerId: 'worker-1', workerCode: 'W-01', workerName: 'عامل الاختبار', percentage: 25, equivalentQuantity: 250, calculatedEarning: 150 }, { workerId: 'worker-2', workerCode: 'W-02', workerName: 'عامل مساعد', percentage: 75, equivalentQuantity: 750, calculatedEarning: 450 }] }, { productModelStageId: secondModelStage.id, stageCode: secondStage.code, stageName: secondStage.name, stageQuantity: 1000, stageCost: 750, compensationMode: 'SharedPercentage', warnings: [secondWarning], workers: [{ workerId: 'worker-1', workerCode: 'W-01', workerName: 'عامل الاختبار', percentage: 100, equivalentQuantity: 1000, calculatedEarning: 750 }] }], workerTotals: [{ workerId: 'worker-1', workerCode: 'W-01', workerName: 'عامل الاختبار', totalEntitlement: 900 }, { workerId: 'worker-2', workerCode: 'W-02', workerName: 'عامل مساعد', totalEntitlement: 450 }], warnings: [firstWarning, secondWarning, 'تحذير عام لا يرتبط بمرحلة محددة.'] };
}

function historicalDraftPreview() {
  return dailyPreview();
}

async function openDailyOperations(page: Page): Promise<void> {
  await page.goto('/manufacturing/daily-production-operations');
  await page.getByLabel('تاريخ الإنتاج').fill('2026-07-17');
  await page.locator('.structure-selector__trigger').click();
  await page.getByPlaceholder('ابحث بالاسم أو الكود').fill(line.lineCode);
  const lineNode = page.locator('.p-treenode-content').filter({
    has: page.locator('.factory-structure-node--line[data-selectable="true"]')
  });
  await expect(lineNode).toBeVisible();
  await expect(async () => {
    await lineNode.locator('.factory-structure-node__content strong').evaluate(element => (element as HTMLElement).click());
    await expect(page.locator('.structure-selector__trigger')).toContainText(line.name, { timeout: 750 });
  }).toPass({ timeout: 5000 });
  await page.getByLabel('الموديل').selectOption(model.id);
  await page.getByRole('button', { name: 'مزامنة حضور التاريخ' }).click();
  await page.getByRole('button', { name: 'تحميل تشغيل اليوم' }).click();
}

async function openSuccessfulDailyPreview(page: Page): Promise<void> {
  await openDailyOperations(page);
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

async function expectSelectedStageAtComfortableOffset(page: Page, productModelStageId: string): Promise<void> {
  const stageRow = page.locator(`[data-stage-id="${productModelStageId}"]`);
  await expect(stageRow).toHaveClass(/is-selected/);
  await expect(stageRow).toHaveAttribute('aria-current', 'true');
  const stageTop = async () => stageRow.evaluate(element => Math.round(element.getBoundingClientRect().top));
  await expect.poll(stageTop).toBeGreaterThanOrEqual(80);
  await expect.poll(stageTop).toBeLessThanOrEqual(140);
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

test('stage-aware validation blockers remain visible and navigate through many invalid stages', async ({ page }) => {
  await preparePage(page, { validationBlockers: true });
  for (const [name, width, height] of [['desktop', 1440, 900], ['tablet-landscape', 1280, 800], ['tablet-portrait', 800, 1280], ['mobile', 390, 844]] as const) {
    await page.setViewportSize({ width, height });
    await openDailyOperations(page);
    await page.getByLabel('كمية تشغيل الخط (تطبق مرة واحدة على كل مرحلة)').fill('1000');
    await page.getByRole('button', { name: 'حساب معاينة موحّدة' }).click();

    const validationPanel = page.locator('.daily-production-operations__validation');
    const validationButtons = validationPanel.locator('button.daily-production-operations__validation-issue');
    await expect(validationButtons).toHaveCount(validationStageDefinitions.length);
    await expect(validationPanel.locator('.daily-production-operations__preview-blocker--global')).toHaveCount(0);
    await expect(page.locator('.daily-production-operations__preview-overview')).toHaveCount(0);
    await expect(validationButtons.first()).toContainText(validationStageDefinitions[0][0]);
    await expect(validationButtons.first()).toContainText(validationStageDefinitions[0][1]);
    await expect(validationButtons.first()).toContainText(validationStageDefinitions[0][2]);
    const firstButtonBox = await validationButtons.first().boundingBox();
    expect(firstButtonBox?.height).toBeGreaterThanOrEqual(44);
    await validationButtons.first().focus();
    await expect(validationButtons.first()).toBeFocused();
    await validationPanel.screenshot({ path: path.join(visualOutput, `daily-validation-blockers-${name}.png`) });

    const contentScroller = page.locator('.plp-app-shell__main');
    const horizontalScrollBefore = await contentScroller.evaluate(element => element.scrollLeft);
    await validationButtons.first().click();
    await expect(page.locator('.daily-production-operations__stage-list > button')).toHaveCount(1);
    await expectSelectedStageAtComfortableOffset(page, validationModelStages[0].id);
    await expect(validationButtons.first()).toHaveClass(/is-active/);
    await expect(validationButtons).toHaveCount(validationStageDefinitions.length);

    await validationButtons.nth(4).click();
    await expect(page.locator('.daily-production-operations__stage-list > button')).toHaveCount(1);
    await expectSelectedStageAtComfortableOffset(page, validationModelStages[4].id);
    await expect(validationButtons.nth(4)).toHaveClass(/is-active/);
    await expect(validationButtons).toHaveCount(validationStageDefinitions.length);
    expect(await contentScroller.evaluate(element => element.scrollLeft)).toBe(horizontalScrollBefore);
    await page.screenshot({ path: path.join(visualOutput, `daily-validation-selected-${name}.png`) });

    await page.getByRole('button', { name: 'عرض كل المراحل' }).click();
    await expect(page.locator('.daily-production-operations__stage-list > button')).toHaveCount(validationStageDefinitions.length);
    await expect(page.locator('.daily-production-operations__stage-list > button.is-selected')).toHaveCount(0);
    await expect(validationButtons).toHaveCount(validationStageDefinitions.length);
    await expectViewportSafe(page);
  }
});

test('inactive persisted STG003 and STG060 stay visible and saveable without operational blockers', async ({ page }) => {
  let previewPayload: any;
  let updatePayload: any;
  await preparePage(page, {
    historicalDraft: true,
    onPreview: payload => { previewPayload = payload; },
    onDraftUpdate: payload => { updatePayload = payload; }
  });
  for (const [name, width, height] of [['desktop', 1440, 900], ['tablet-landscape', 1280, 800], ['tablet-portrait', 800, 1280], ['mobile', 390, 844]] as const) {
    previewPayload = undefined;
    updatePayload = undefined;
    await page.setViewportSize({ width, height });
    await openDailyOperations(page);
    await page.getByRole('button', { name: 'حساب معاينة موحّدة' }).click();

    const issues = page.locator('button.daily-production-operations__validation-issue');
    await expect(issues).toHaveCount(1);
    await expect(issues.first()).toContainText('W-ACTIVE');
    await expect(issues.first()).not.toContainText('STG003');
    await expect(issues.first()).not.toContainText('STG060');
    await expect(page.getByText('سبب التجاوز مطلوب للعامل W003.')).toHaveCount(0);
    await expect(page.getByText('سبب التجاوز مطلوب للعامل W060.')).toHaveCount(0);

    await issues.first().click();
    await page.getByLabel('سبب التجاوز المعتمد').fill('اعتماد تشغيل المرحلة النشطة');
    await page.getByRole('button', { name: 'حساب معاينة موحّدة' }).click();
    await expect(page.locator('.daily-production-operations__preview-overview')).toBeVisible();
    await expect.poll(() => previewPayload?.stages?.length ?? 0).toBe(2);
    expect(previewPayload.stages.map((item: any) => item.productModelStageId)).toEqual([modelStage.id, secondModelStage.id]);
    expect(previewPayload.stages.some((item: any) => item.productModelStageId === 'historical-stage-003')).toBe(false);
    expect(previewPayload.stages.some((item: any) => item.productModelStageId === 'historical-stage-060')).toBe(false);

    await page.getByRole('button', { name: 'عرض كل المراحل' }).click();
    await expect(page.locator('.daily-production-operations__stage-list > button')).toHaveCount(4);
    await page.locator(`[data-stage-id="${secondModelStage.id}"]`).click();
    await expect(page.locator('.daily-production-operations__detail-panel')).toContainText(secondStage.name);
    await expect(page.locator('.daily-production-operations__detail-panel input:enabled')).not.toHaveCount(0);
    await page.locator('[data-stage-id="historical-stage-003"]').click();
    await expect(page.locator('.daily-production-operations__detail-panel')).toContainText('مرحلة محفوظة غير نشطة');
    await expect(page.locator('.daily-production-operations__detail-panel')).toContainText('ستظل بياناتها التاريخية محفوظة للقراءة فقط');
    await expect(page.locator('.daily-production-operations__detail-panel input:enabled')).toHaveCount(0);
    await page.screenshot({ path: path.join(visualOutput, `daily-historical-inactive-${name}.png`), fullPage: true });

    await page.getByRole('button', { name: 'حفظ تغييرات مسودة اليوم' }).click();
    await expect.poll(() => updatePayload?.stages?.length ?? 0).toBe(3);
    expect(updatePayload.stages.map((item: any) => [item.productModelStageId, item.stageProductionRecordId])).toEqual([
      [modelStage.id, 'record-active'],
      ['historical-stage-003', 'record-003'],
      ['historical-stage-060', 'record-060']
    ]);
    expect(updatePayload.previewToken).toBeNull();
    expect(updatePayload.stages.some((item: any) => item.productModelStageId === secondModelStage.id)).toBe(false);
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
    const contentScroller = page.locator('.plp-app-shell__main');
    const horizontalScrollBefore = await contentScroller.evaluate(element => element.scrollLeft);
    await blockerButtons.first().click();
    await expect(page.locator('.daily-production-operations__stage-list > button')).toHaveCount(1);
    await expectSelectedStageAtComfortableOffset(page, modelStage.id);
    expect(await contentScroller.evaluate(element => element.scrollLeft)).toBe(horizontalScrollBefore);
    await blockerButtons.nth(1).click();
    await expect(page.locator('.daily-production-operations__stage-list > button')).toHaveCount(1);
    await expectSelectedStageAtComfortableOffset(page, secondModelStage.id);
    await expect(page.locator('.daily-production-operations__active-stage-filter')).toContainText(secondStage.name);
    await page.screenshot({ path: path.join(visualOutput, `daily-selected-stage-${name}.png`) });
    await page.getByRole('button', { name: 'عرض كل المراحل' }).click();
    await expect(page.locator('.daily-production-operations__stage-list > button')).toHaveCount(2);
    await expect(page.locator('.daily-production-operations__stage-list > button.is-selected')).toHaveCount(0);
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
