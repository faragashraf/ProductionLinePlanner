import { expect, Page, test } from '@playwright/test';
import { mkdir } from 'node:fs/promises';
import path from 'node:path';

const visualOutput = path.join(process.cwd(), 'test-results', 'manufacturing-command-center');
const permissions = ['dashboard.view', 'factory-structure.view', 'stages.view', 'production.view'];
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

test.beforeAll(async () => { await mkdir(visualOutput, { recursive: true }); });

async function preparePage(page: Page): Promise<void> {
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
    const data = pathname.endsWith('/api/auth/me')
      ? { id: 'visual-user', fullName: 'مدير المصنع', email: 'visual@local.test', roles: ['Administrator'], permissions }
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
    await expect(page.getByRole('heading', { name: 'خريطة المصنع التشغيلية' })).toBeVisible();
    await page.locator('.department-node > summary').click();
    await expect(page.locator('.line-node')).toHaveCount(3);
    await expect(page.locator('.line-node').first()).toContainText(criticalLine.name);
    await expect(page.locator('.line-node').first()).toHaveClass(/line-state-critical/);
    await expect(page.locator('.line-node').first().locator('.line-dimensions > span')).toHaveCount(4);
    await expectViewportSafe(page);
    await page.screenshot({ path: path.join(visualOutput, `${name}-factory-map.png`), fullPage: true });
  }

  await page.goto('/manufacturing/dashboard');
  await expect(page.getByRole('heading', { name: 'لوحة تحكم التصنيع' })).toBeVisible();
  await expect(page.getByText('ستظهر بيانات كل مجال')).toHaveCount(0);
});

test('hides previous-scope dashboard figures and map nodes while a filter response is pending', async ({ page }) => {
  await preparePage(page);
  await page.setViewportSize({ width: 1280, height: 800 });

  await page.goto('/dashboard');
  await expect(page.locator('.metric-grid').first()).toBeVisible();
  await page.locator('.command-filters__field select').nth(0).selectOption({ label: 'مصنع الأحذية الرئيسي · F-01' });
  await expect(page.locator('.command-page__loading-notice')).toBeVisible();
  await expect(page.locator('.metric-grid')).toHaveCount(0);
  await expect(page.locator('.metric-grid').first()).toBeVisible();

  await page.goto('/factory-map');
  await expect(page.locator('.factory-tree')).toBeVisible();
  await page.locator('.command-filters__field select').nth(0).selectOption({ label: 'مصنع الأحذية الرئيسي · F-01' });
  await expect(page.locator('.factory-command__loading-notice')).toBeVisible();
  await expect(page.locator('.factory-tree')).toHaveCount(0);
  await expect(page.locator('.factory-tree')).toBeVisible();
});
