import { expect, Page, test } from '@playwright/test';
import { mkdir } from 'node:fs/promises';
import path from 'node:path';

const visualOutput = path.join(process.cwd(), 'test-results', 'manufacturing-command-center');
const permissions = ['dashboard.view', 'factory-structure.view', 'stages.view', 'assignments.view', 'attendance.view', 'production.view'];
const viewports = [
  ['desktop-1440x900', 1440, 900],
  ['tablet-landscape-1280x800', 1280, 800],
  ['tablet-portrait-800x1280', 800, 1280],
  ['mobile-390x844', 390, 844]
] as const;

const stage = {
  productModelStageId: 'model-stage-1', subStageId: 'stage-1', mainStageName: 'التجهيز', stageCode: 'ST-01', stageName: 'القص',
  stageOrder: 1, requiredWorkers: 4, permanentlyAssignedWorkers: 3, presentPermanentlyAssignedWorkers: 2,
  hasPrice: true, hasStandardTime: true, isRegistered: true, alerts: []
};

function operation(id: string, lineId: string, status: 'Draft' | 'Approved' | 'Cancelled') {
  return {
    productionOrderId: id, productionLineId: lineId, productModelId: 'model-1', productModelCode: 'SH-01', productModelName: 'موديل الحذاء اليومي',
    status, finalLineQuantity: 120, recordedStageValue: 480, registeredStages: 1, journeyStages: 1,
    stageRegistrationCoverage: { numerator: 1, denominator: 1, percentage: 100, scope: 'الخط', date: '2026-07-24', zeroBehavior: 'Calculated' },
    lastReliableUpdateUtc: '2026-07-24T07:00:00Z', stages: [stage]
  };
}

const cancelledOperation = operation('order-cancelled', 'line-critical', 'Cancelled');
const draftOperation = operation('order-draft', 'line-warning', 'Draft');
const approvedOperation = operation('order-approved', 'line-healthy', 'Approved');
const criticalLine = {
  id: 'line-critical', factoryId: 'factory-1', departmentId: 'department-1', name: 'خط القص الحرج', code: 'L-01', readinessStatus: 'StaffingShortage',
  permanentlyAssignedWorkers: 3, presentPermanentlyAssignedWorkers: 2, requiredWorkers: 4, journeyStages: 1,
  stagesCoveredByPresentWorker: 0, stagesWithoutPresentWorker: 1, lastReliableUpdateUtc: '2026-07-24T07:00:00Z',
  alerts: ['مرحلة القص بلا تغطية كافية'], operations: [cancelledOperation]
};
const warningLine = {
  ...criticalLine, id: 'line-warning', name: 'خط التجميع', code: 'L-02', readinessStatus: 'Ready',
  permanentlyAssignedWorkers: 4, presentPermanentlyAssignedWorkers: 4, requiredWorkers: 4, stagesCoveredByPresentWorker: 1,
  stagesWithoutPresentWorker: 0, alerts: [], operations: [draftOperation]
};
const healthyLine = {
  ...warningLine, id: 'line-healthy', name: 'خط التشطيب المستقر', code: 'L-03', operations: [approvedOperation]
};

const commandCenter = {
  scope: { productionDate: '2026-07-24', factoryId: null, departmentId: null, productionLineId: null, operationStatus: 'All', description: 'كل المصانع والأقسام والخطوط' },
  filterCatalog: {
    factories: [{ id: 'factory-1', name: 'مصنع الأحذية الرئيسي', code: 'F-01' }],
    departments: [{ id: 'department-1', factoryId: 'factory-1', name: 'قسم الإنتاج', code: 'PROD' }],
    lines: [criticalLine, warningLine, healthyLine].map(line => ({ id: line.id, factoryId: 'factory-1', departmentId: 'department-1', name: line.name, code: line.code }))
  },
  workforce: {
    activeWorkers: 28, presentWorkers: 24, presentPermanentlyAssignedWorkers: 22, presentUnassignedWorkers: 2,
    permanentlyAssignedNotPresentWorkers: 4,
    assignmentCoverage: { numerator: 22, denominator: 24, percentage: 91.7, scope: 'المصنع', date: '2026-07-24', zeroBehavior: 'Calculated' },
    attendanceEvidenceComplete: true, attributionNote: 'الحضور والتسكين الدائم في النطاق المحدد.', presentAssignedDetails: [], presentUnassignedDetails: [], assignedNotPresentDetails: []
  },
  lineSummary: { activeLines: 3, readyLines: 2, staffingShortageLines: 1, journeyNotConfiguredLines: 0, dataIncompleteLines: 0, problemLines: 2, stagesWithoutPresentWorker: 1 },
  operations: { linesWithOperation: 3, linesWithoutOperation: 0, draftOperations: 1, approvedOperations: 1, approvalCancelledOperations: 0, cancelledOperations: 1, approvedRecordedValue: 480, items: [cancelledOperation, draftOperation, approvedOperation] },
  dataQuality: { modelStagesWithoutPrice: 0, modelStagesWithoutStandardTime: 0, activeJourneyStagesWithoutPresentWorker: 1, activeModelsWithoutJourney: 0, issues: [], modelsWithoutJourneyScopeNote: 'النطاق الكامل' },
  factories: [{
    id: 'factory-1', name: 'مصنع الأحذية الرئيسي', code: 'F-01', activeDepartments: 1, activeLines: 3,
    presentPermanentlyAssignedWorkers: 22, problemLines: 2, draftOperations: 1, approvedOperations: 1,
    departments: [{
      id: 'department-1', name: 'قسم الإنتاج', code: 'PROD', activeLines: 3, presentPermanentlyAssignedWorkers: 22,
      permanentlyAssignedWorkers: 26, presentUnassignedWorkers: 2, readyLines: 2, notReadyLines: 1, draftOperations: 1,
      approvedOperations: 1, workforceAttributionNote: 'تسكين دائم نشط', lines: [healthyLine, warningLine, criticalLine]
    }]
  }],
  calculatedAtUtc: '2026-07-24T07:00:00Z'
};

const freshness = {
  status: 'Fresh', isTrusted: true, lastAttemptAtUtc: '2026-07-29T07:00:00Z',
  lastSuccessfulAtUtc: '2026-07-29T07:00:00Z', lastErrorCode: null, ageMinutes: 0
};
const readinessMetrics = (present: number, assigned: number, late: number, absent: number, checkedOut: number, childCount: number) => ({
  assignedWorkerCount: assigned, currentlyPresentCount: present, lateCount: late, absentCount: absent,
  checkedOutCount: checkedOut, unknownCount: 0, operationalReadinessPercentage: assigned ? Math.round(present * 1000 / assigned) / 10 : null,
  contributionToParentShortage: assigned ? assigned - present : null, childCount,
  status: assigned === 0 ? 'NoAssignments' : present / assigned >= .9 ? 'Ready' : present / assigned >= .7 ? 'Warning' : 'Critical'
});
const readinessSnapshot = {
  operationalDate: '2026-07-29', calculatedAtUtc: '2026-07-29T07:00:00Z', attendanceSync: freshness,
  workdayPolicy: { workdayBoundaryTime: '05:00', dayStartTime: '08:00', gracePeriodMinutes: 15, freshnessThresholdMinutes: 5 },
  factories: [{
    id: 'factory-1', name: 'مصنع الأحذية الرئيسي', code: 'F-01', metrics: readinessMetrics(6, 10, 2, 3, 1, 1),
    departments: [{
      id: 'department-1', factoryId: 'factory-1', name: 'قسم الإنتاج', code: 'PROD', metrics: readinessMetrics(6, 10, 2, 3, 1, 2),
      productionLines: [
        { id: 'line-critical', factoryId: 'factory-1', departmentId: 'department-1', name: 'خط القص الحرج', code: 'L-01', metrics: readinessMetrics(3, 6, 1, 2, 1, 2), modelNames: ['موديل الحذاء اليومي', 'موديل السلامة'], models: [{ id: 'model-1', name: 'موديل الحذاء اليومي', code: 'SH-01', stageCount: 4 }, { id: 'model-2', name: 'موديل السلامة', code: 'SH-02', stageCount: 1 }] },
        { id: 'line-ready', factoryId: 'factory-1', departmentId: 'department-1', name: 'خط التشطيب', code: 'L-02', metrics: readinessMetrics(3, 4, 1, 1, 0, 1), modelNames: ['موديل الحذاء اليومي'], models: [{ id: 'model-1', name: 'موديل الحذاء اليومي', code: 'SH-01', stageCount: 1 }] }
      ]
    }]
  }]
};
const readinessStages = {
  operationalDate: '2026-07-29', calculatedAtUtc: '2026-07-29T07:00:00Z', attendanceSync: freshness,
  factoryId: 'factory-1', factoryName: 'مصنع الأحذية الرئيسي', departmentId: 'department-1', departmentName: 'قسم الإنتاج',
  productionLineId: 'line-critical', productionLineName: 'خط القص الحرج', selectedProductModelId: 'model-1', selectedProductModelName: 'موديل الحذاء اليومي', requiresModelSelection: false, availableModels: [{ id: 'model-1', name: 'موديل الحذاء اليومي', code: 'SH-01', stageCount: 4 }, { id: 'model-2', name: 'موديل السلامة', code: 'SH-02', stageCount: 1 }], stages: [
    { id: 'stage-cut', factoryId: 'factory-1', departmentId: 'department-1', productionLineId: 'line-critical', mainStageId: 'main-1', name: 'القص', code: 'ST-01', mainStageName: 'التجهيز', stageOrder: 30, metrics: readinessMetrics(2, 3, 1, 1, 1, 3), modelNames: ['موديل الحذاء اليومي'] },
    { id: 'stage-sew', factoryId: 'factory-1', departmentId: 'department-1', productionLineId: 'line-critical', mainStageId: 'main-2', name: 'الحياكة', code: 'ST-02', mainStageName: 'الخياطة', stageOrder: 10, metrics: readinessMetrics(2, 2, 0, 0, 0, 2), modelNames: ['موديل الحذاء اليومي'] },
    { id: 'stage-late', factoryId: 'factory-1', departmentId: 'department-1', productionLineId: 'line-critical', mainStageId: 'main-3', name: 'التجميع', code: 'ST-03', mainStageName: 'التجميع', stageOrder: 20, metrics: readinessMetrics(2, 2, 1, 0, 0, 2), modelNames: ['موديل الحذاء اليومي'] },
    { id: 'stage-empty', factoryId: 'factory-1', departmentId: 'department-1', productionLineId: 'line-critical', mainStageId: 'main-4', name: 'التغليف', code: 'ST-04', mainStageName: 'التشطيب', stageOrder: null, metrics: readinessMetrics(0, 0, 0, 0, 0, 0), modelNames: ['موديل الحذاء اليومي'] }
  ]
};
const readinessWorkers = {
  operationalDate: '2026-07-29', calculatedAtUtc: '2026-07-29T07:00:00Z', attendanceSync: freshness,
  factoryId: 'factory-1', factoryName: 'مصنع الأحذية الرئيسي', departmentId: 'department-1', departmentName: 'قسم الإنتاج',
  productionLineId: 'line-critical', productionLineName: 'خط القص الحرج', stageId: 'stage-cut', stageName: 'القص', workers: [
    { workerId: 'worker-absent', productionLineId: 'line-critical', stageId: 'stage-cut', employeeCode: 'W-103', fullName: 'محمود سمير', attendanceState: 'NotCheckedIn', attendanceLabel: 'لم يسجل حضورًا', isOperationallyPresent: false, checkInAtUtc: null, checkOutAtUtc: null, lateByMinutes: null },
    { workerId: 'worker-out', productionLineId: 'line-critical', stageId: 'stage-cut', employeeCode: 'W-102', fullName: 'أحمد صالح', attendanceState: 'Present', attendanceLabel: 'حاضر', isOperationallyPresent: true, checkInAtUtc: '2026-07-29T05:00:00Z', checkOutAtUtc: '2026-07-29T09:00:00Z', lateByMinutes: null },
    { workerId: 'worker-late', productionLineId: 'line-critical', stageId: 'stage-cut', employeeCode: 'W-101', fullName: 'علي حسن', attendanceState: 'Late', attendanceLabel: 'متأخر', isOperationallyPresent: true, checkInAtUtc: '2026-07-29T05:20:00Z', checkOutAtUtc: null, lateByMinutes: 20 }
  ]
};

test.beforeAll(async () => { await mkdir(visualOutput, { recursive: true }); });

async function preparePage(page: Page, readiness: 'success' | 'empty' | 'error' | 'delayed' = 'success'): Promise<void> {
  await page.addInitScript(({ userPermissions }) => {
    localStorage.setItem('plp.accessToken', 'command-center-visual-token');
    localStorage.setItem('plp.currentUser', JSON.stringify({ id: 'visual-user', fullName: 'مدير المصنع', email: 'visual@local.test', roles: ['Administrator'], permissions: userPermissions }));
  }, { userPermissions: permissions });

  await page.routeWebSocket('**/hubs/notifications**', socket => socket.onMessage(message => {
    if (typeof message !== 'string') return;
    if (message.includes('"protocol"')) socket.send('{}\u001e');
    else if (message.includes('"invocationId"')) {
      const invocationId = JSON.parse(message.replace('\u001e', '')).invocationId;
      socket.send(`${JSON.stringify({ type: 3, invocationId, result: true })}\u001e`);
    }
  }));
  await page.route('**/hubs/notifications/negotiate**', route => route.fulfill({
    status: 200, contentType: 'application/json',
    body: JSON.stringify({ connectionId: 'command-center', connectionToken: 'command-center', availableTransports: [{ transport: 'WebSockets', transferFormats: ['Text', 'Binary'] }] })
  }));
  await page.route('**/api/**', async route => {
    const url = new URL(route.request().url());
    const pathname = url.pathname;
    if (pathname.endsWith('/api/manufacturing-command-center') && url.searchParams.has('factoryId')) {
      await new Promise(resolve => setTimeout(resolve, 500));
    }
    if (pathname.endsWith('/api/operational-readiness') && readiness === 'error') {
      await route.fulfill({ status: 503, contentType: 'application/json', body: JSON.stringify({ success: false, data: null, error: { message: 'غير متاح' } }) });
      return;
    }
    if (pathname.endsWith('/api/operational-readiness') && readiness === 'delayed') await new Promise(resolve => setTimeout(resolve, 450));
    const data = pathname.endsWith('/api/auth/me')
      ? { id: 'visual-user', fullName: 'مدير المصنع', email: 'visual@local.test', roles: ['Administrator'], permissions }
      : pathname.endsWith('/api/operational-readiness')
        ? readiness === 'empty' ? { ...readinessSnapshot, factories: [] } : readinessSnapshot
        : pathname.endsWith('/workers') && pathname.includes('/api/operational-readiness/lines/') ? readinessWorkers
        : pathname.endsWith('/stages') && pathname.includes('/api/operational-readiness/lines/') ? readinessStages
        : pathname.endsWith('/api/manufacturing-command-center') ? commandCenter : { items: [] };
    await route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify({ success: true, data, error: null }) });
  });
}

async function expectViewportSafe(page: Page): Promise<void> {
  const geometry = await page.evaluate(() => ({
    direction: getComputedStyle(document.documentElement).direction,
    rootOverflow: document.documentElement.scrollWidth - document.documentElement.clientWidth,
    mainOverflow: (document.querySelector('.plp-app-shell__main')?.scrollWidth ?? 0) - (document.querySelector('.plp-app-shell__main')?.clientWidth ?? 0)
  }));
  expect(geometry.direction).toBe('rtl');
  expect(geometry.rootOverflow).toBeLessThanOrEqual(1);
  expect(geometry.mainOverflow).toBeLessThanOrEqual(1);
}

test('reviews dashboard and factory map across the required RTL viewports', async ({ page }) => {
  await preparePage(page);
  for (const [name, width, height] of viewports) {
    await page.setViewportSize({ width, height });
    await page.goto('/dashboard');
    await expect(page.getByRole('heading', { name: 'لوحة تحكم التصنيع' })).toBeVisible();
    await expect(page.locator('.problem-lines > li')).toHaveCount(2);
    await expect(page.locator('.problem-lines')).not.toContainText(healthyLine.name);
    await expect(page.locator('.problem-lines > li').first().locator('.line-dimensions > span')).toHaveCount(4);
    await expect(page.getByText('أجور عمال مسجلة لعمليات معتمدة')).toBeVisible();
    await expect(page.getByText('متصل لحظيًا')).toBeVisible();
    await expectViewportSafe(page);
    await page.screenshot({ path: path.join(visualOutput, `${name}-dashboard.png`), fullPage: true });

    await page.goto('/factory-map');
    await expect(page.getByRole('heading', { name: 'جاهزية تشغيل المصنع الآن' })).toBeVisible();
    await page.getByRole('button', { name: /مصنع الأحذية الرئيسي/ }).click();
    await page.getByRole('button', { name: /قسم الإنتاج/ }).click();
    await page.getByRole('button', { name: /خط القص الحرج/ }).click();
    await expect(page.getByText('اختر موديلًا لعرض مراحله')).toBeVisible();
    await page.locator('app-readiness-model-selector').getByRole('button', { name: /موديل الحذاء اليومي/ }).click();
    await page.getByRole('button', { name: /^القص ST-01/ }).click();
    await expect(page.locator('app-worker-attendance-status')).toHaveCount(3);
    await page.getByRole('button', { name: 'متأخر', exact: true }).click();
    await expect(page.locator('app-worker-attendance-status')).toHaveCount(1);
    await expect(page.locator('app-worker-attendance-status')).toContainText('علي حسن');
    await expect(page.locator('app-worker-attendance-status')).toContainText('تأخير 20 د');
    await expectViewportSafe(page);
    await page.screenshot({ path: path.join(visualOutput, `${name}-factory-map.png`), fullPage: true });
  }

  await page.goto('/manufacturing/dashboard');
  await expect(page.getByRole('heading', { name: 'لوحة تحكم التصنيع' })).toBeVisible();
  await expect(page.getByText('ستظهر بيانات كل مجال')).toHaveCount(0);
});

test('hides previous-scope dashboard figures while a filter response is pending', async ({ page }) => {
  await preparePage(page);
  await page.setViewportSize({ width: 1280, height: 800 });

  await page.goto('/dashboard');
  await expect(page.locator('.metric-grid').first()).toBeVisible();
  await page.locator('.command-filters__field select').nth(0).selectOption({ label: 'مصنع الأحذية الرئيسي · F-01' });
  await expect(page.locator('.command-page__loading-notice')).toBeVisible();
  await expect(page.locator('.metric-grid')).toHaveCount(0);
  await expect(page.locator('.metric-grid').first()).toBeVisible();
});

test('filters model stages by multiple issue types on an Android tablet', async ({ page }) => {
  await preparePage(page);
  await page.setViewportSize({ width: 1280, height: 800 });
  await page.goto('/factory-map');
  await page.getByRole('button', { name: /مصنع الأحذية الرئيسي/ }).click();
  await page.getByRole('button', { name: /قسم الإنتاج/ }).click();
  await page.getByRole('button', { name: /خط القص الحرج/ }).click();
  await page.locator('app-readiness-model-selector').getByRole('button', { name: /موديل الحذاء اليومي/ }).click();

  const filter = page.locator('app-readiness-stage-filter');
  const stageCards = page.locator('.readiness-map__grid app-readiness-node-card');
  await expect(filter.getByText('عرض 4 من 4 مرحلة')).toBeVisible();
  await expect(stageCards).toHaveCount(4);
  await expect(stageCards.nth(0)).toContainText('الحياكة');
  await expect(stageCards.nth(1)).toContainText('التجميع');
  await expect(stageCards.nth(2)).toContainText('القص');
  await expect(stageCards.nth(3)).toContainText('التغليف');

  await filter.locator('.p-multiselect').click();
  await page.locator('.p-multiselect-panel:visible').last().getByRole('option', { name: /بها غائبون/ }).click();
  await expect(filter.getByText('عرض 1 من 4 مرحلة')).toBeVisible();
  await filter.locator('.p-multiselect').click();
  await page.locator('.p-multiselect-panel:visible').last().getByRole('option', { name: /بها متأخرون/ }).click();
  await expect(filter.locator('.p-multiselect-label')).toContainText('2 أنواع مشكلات');
  await expect(filter.getByText('عرض 2 من 4 مرحلة')).toBeVisible();
  await expect(stageCards).toHaveCount(2);
  await expect(stageCards.nth(0)).toContainText('التجميع');
  await expect(stageCards.nth(1)).toContainText('القص');

  await page.getByRole('button', { name: /^القص ST-01/ }).click();
  await page.locator('.readiness-map__breadcrumb').getByRole('button', { name: 'خط القص الحرج', exact: true }).click();
  await expect(filter.getByText('عرض 2 من 4 مرحلة')).toBeVisible();

  await filter.getByRole('button', { name: 'مسح الفلاتر' }).click();
  await expect(filter.getByText('عرض 4 من 4 مرحلة')).toBeVisible();
  await filter.locator('.p-multiselect').click();
  await page.locator('.p-multiselect-panel:visible').last().getByRole('option', { name: /حضور غير مؤكد/ }).click();
  await expect(page.getByText('لا توجد مراحل تطابق أنواع المشكلات المحددة')).toBeVisible();
  await page.locator('.readiness-map__empty--filtered').getByRole('button', { name: 'مسح الفلاتر' }).click();
  await expect(stageCards).toHaveCount(4);
  await expect(page.locator('.p-multiselect-panel:visible')).toHaveCount(0);
  await expectViewportSafe(page);
  await page.screenshot({ path: path.join(visualOutput, 'android-tablet-stage-filters.png'), fullPage: true });
});

test('shows an honest readiness loading state before rendering the hierarchy', async ({ page }) => {
  await preparePage(page, 'delayed');
  await page.goto('/factory-map');
  await expect(page.locator('.factory-readiness__loading')).toBeVisible();
  await expect(page.getByRole('button', { name: /مصنع الأحذية الرئيسي/ })).toBeVisible();
});

test('shows readiness empty state without a fabricated 100% value', async ({ page }) => {
  await preparePage(page, 'empty');
  await page.goto('/factory-map');
  await expect(page.getByText('لا توجد مصانع نشطة')).toBeVisible();
  await expect(page.getByText('100%')).toHaveCount(0);
});

test('shows an actionable readiness error state', async ({ page }) => {
  await preparePage(page, 'error');
  await page.goto('/factory-map');
  await expect(page.getByRole('alert')).toContainText('تعذر تحميل خريطة الجاهزية');
  await expect(page.getByRole('button', { name: 'إعادة المحاولة' })).toBeVisible();
});
