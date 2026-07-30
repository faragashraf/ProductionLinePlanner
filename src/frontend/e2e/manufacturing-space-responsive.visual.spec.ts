import { expect, Page, test } from '@playwright/test';
import { mkdir } from 'node:fs/promises';
import path from 'node:path';

const visualOutput = path.join(process.cwd(), 'test-results', 'manufacturing-space-responsive');
const permissions = [
  'workers.view', 'workers.manage', 'departments.view', 'departments.manage',
  'attendance.view', 'attendance.sync', 'factory-structure.view', 'factory-structure.manage',
  'assignments.view', 'assignments.manage', 'stages.view', 'stages.manage',
  'models.view', 'models.manage', 'production.view', 'production.record', 'production.approve',
  'reports.production.view', 'reports.financial.view'
];

const factory = { id: 'factory-1', code: 'F-01', name: 'مصنع الاختبار المتجاوب', location: 'القاهرة', isActive: true };
const department = { id: 'department-1', factoryId: factory.id, code: 'CUT', nameAr: 'قسم القص والتجهيز', nameEn: 'Cutting', sequenceOrder: 1, productionLineCount: 1, isActive: true };
const line = { id: 'line-1', factoryId: factory.id, departmentId: department.id, departmentCode: department.code, departmentNameAr: department.nameAr, lineCode: 'L-01', name: 'خط الإنتاج الرئيسي طويل الاسم', sequenceOrder: 1, isActive: true };
const otherLine = { id: 'line-2', name: 'خط التجميع المساند' };
const model = { id: 'model-1', code: 'MODEL-01', name: 'موديل التشغيل التجريبي', isActive: true };
const subStageIds = [
  '11111111-1111-1111-1111-111111111111',
  '22222222-2222-2222-2222-222222222222',
  '33333333-3333-3333-3333-333333333333'
];

const workers = Array.from({ length: 8 }, (_, index) => ({
  workerId: `aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaa${index}`,
  employeeCode: `W-${String(index + 1).padStart(3, '0')}`,
  fullName: index === 0 ? 'عامل تشغيل باسم عربي طويل لاختبار الالتفاف الآمن داخل اللوحي' : `عامل التشغيل ${index + 1}`,
  departmentName: department.nameAr,
  isOnActiveService: true,
  hasPhoto: false,
  photoReference: null,
  photoVersion: null,
  defaultSubStageId: index < 2 ? subStageIds[index] : null,
  defaultSubStageName: index < 2 ? (index === 0 ? 'مرحلة القص والتجهيز الأولي' : 'مرحلة التشغيل الثانية') : null,
  effectiveAssignmentId: index < 2 ? `assignment-${index + 1}` : null,
  effectiveAssignmentType: index < 2 ? 'Default' : null,
  effectiveSubStageId: index < 2 ? subStageIds[index] : null,
  effectiveSubStageName: index < 2 ? (index === 0 ? 'مرحلة القص والتجهيز الأولي' : 'مرحلة التشغيل الثانية') : null,
  fromSubStageId: null,
  fromSubStageName: null,
  temporaryStartsAtUtc: null,
  temporaryEndsAtUtc: null,
  replacementForWorkerId: null,
  participations: index < 2 ? [{
    assignmentId: `assignment-${index + 1}`,
    assignmentType: 'Default',
    productionLineId: index === 0 ? line.id : otherLine.id,
    productionLineName: index === 0 ? line.name : otherLine.name,
    subStageId: subStageIds[index],
    subStageName: index === 0 ? 'مرحلة القص والتجهيز الأولي' : 'مرحلة التشغيل الثانية',
    fromSubStageId: null,
    fromSubStageName: null,
    startsAtUtc: '2026-07-01T06:00:00Z',
    endsAtUtc: null,
    replacementForWorkerId: null,
    temporaryParticipationMode: null
  }] : []
}));

const stages = subStageIds.map((subStageId, index) => ({
  productModelStageId: `model-stage-${index + 1}`,
  subStageId,
  mainStageName: index === 0 ? 'التجهيز' : 'التشغيل',
  stageCode: `ST-${String(index + 1).padStart(2, '0')}`,
  stageName: index === 0 ? 'مرحلة القص والتجهيز الأولي ذات الاسم الطويل' : `مرحلة التشغيل ${index + 1}`,
  stageOrder: index + 1,
  piecePrice: 2.5 + index,
  compensationMode: 'SharedPercentage',
  compensationConfigurationStatus: 'Configured',
  isFinancialReviewPending: false,
  defaultAssignedWorkersCount: index === 0 ? 1 : 0,
  effectiveAssignedWorkersCount: index === 0 ? 1 : 0,
  temporaryAssignedWorkersCount: 0,
  requiredWorkers: index === 0 ? 2 : null,
  hasAuthoritativeRequiredWorkerCount: index === 0,
  staffingStatus: index === 0 ? 'Ready' : 'NeedsStaffingReview',
  workerStatusText: index === 0 ? 'التسكين مطابق للعدد المطلوب' : 'تحتاج المرحلة إلى تسكين',
  effectiveWorkerIds: index === 0 ? [workers[0].workerId] : []
}));

const staffingPlan = {
  factoryId: factory.id,
  factoryName: factory.name,
  productionLineId: line.id,
  productionLineName: line.name,
  productModelId: model.id,
  productModelCode: model.code,
  productModelName: model.name,
  staffingReferenceDate: '2026-07-21',
  totalStages: stages.length,
  stagesWithWorkers: 1,
  stagesWithoutWorkers: 2,
  stagesWithTemporaryAssignments: 0,
  stagesNeedingCompensationReview: 0,
  stagesNeedingStaffingReview: 2,
  overallStaffingStatus: 'NeedsStaffingReview',
  staffingPlanComplete: false,
  operationalAttendanceChecked: false,
  financialConfigurationPending: false,
  stages,
  workers
};

const exactViewports = [
  ['mobile-360x800', 360, 800], ['mobile-390x844', 390, 844], ['mobile-412x915', 412, 915],
  ['android-portrait-600x960', 600, 960], ['android-portrait-800x1280', 800, 1280], ['android-portrait-962x1280', 962, 1280],
  ['android-landscape-960x600', 960, 600], ['android-landscape-1280x800', 1280, 800], ['android-landscape-1280x962', 1280, 962],
  ['ipad-portrait-768x1024', 768, 1024], ['ipad-portrait-820x1180', 820, 1180], ['ipad-portrait-1024x1366', 1024, 1366],
  ['desktop-1366x768', 1366, 768], ['desktop-1440x900', 1440, 900], ['desktop-1920x1080', 1920, 1080],
  ['between-700x1100', 700, 1100], ['between-1000x720', 1000, 720], ['between-1100x850', 1100, 850]
] as const;

interface PageDiagnostics {
  consoleErrors: string[];
  failedRequests: string[];
  errorResponses: string[];
}

test.beforeAll(async () => { await mkdir(visualOutput, { recursive: true }); });

function response(data: unknown) {
  return JSON.stringify({ success: true, data, error: null });
}

async function preparePage(page: Page): Promise<PageDiagnostics> {
  const diagnostics: PageDiagnostics = { consoleErrors: [], failedRequests: [], errorResponses: [] };
  page.on('console', message => {
    if (message.type() === 'error' && !diagnostics.consoleErrors.includes(message.text())) diagnostics.consoleErrors.push(message.text());
  });
  page.on('pageerror', error => {
    if (!diagnostics.consoleErrors.includes(error.message)) diagnostics.consoleErrors.push(error.message);
  });
  page.on('requestfailed', request => diagnostics.failedRequests.push(`${request.method()} ${request.url()} — ${request.failure()?.errorText ?? 'failed'}`));
  page.on('response', responseValue => {
    if (responseValue.status() >= 400) diagnostics.errorResponses.push(`${responseValue.status()} ${responseValue.url()}`);
  });
  await page.addInitScript(({ userPermissions }) => {
    localStorage.setItem('plp.accessToken', 'manufacturing-responsive-visual-token');
    localStorage.setItem('plp.currentUser', JSON.stringify({
      id: 'visual-user', fullName: 'مراجع مساحة التصنيع', email: 'manufacturing.qa@local.test',
      roles: ['Administrator'], permissions: userPermissions
    }));
  }, { userPermissions: permissions });

  await page.routeWebSocket('**/hubs/notifications**', socket => socket.onMessage(message => {
    if (typeof message === 'string' && message.includes('"protocol"')) socket.send('{}\u001e');
  }));
  await page.route('**/hubs/notifications/negotiate**', route => route.fulfill({
    status: 200,
    contentType: 'application/json',
    body: JSON.stringify({ connectionId: 'manufacturing-responsive', connectionToken: 'manufacturing-responsive', availableTransports: [{ transport: 'WebSockets', transferFormats: ['Text', 'Binary'] }] })
  }));

  await page.route('**/api/**', async route => {
    const url = new URL(route.request().url());
    const pathname = url.pathname;
    let data: unknown = { items: [] };

    if (pathname.endsWith('/api/auth/me')) data = { id: 'visual-user', fullName: 'مراجع مساحة التصنيع', email: 'manufacturing.qa@local.test', roles: ['Administrator'], permissions };
    else if (pathname.endsWith('/api/line-staffing/workers')) data = workers;
    else if (pathname.endsWith('/api/line-staffing')) data = staffingPlan;
    else if (pathname.endsWith('/api/factory-structure/delete-eligibility')) data = { factories: [{ entityId: factory.id, canDelete: false }], departments: [{ entityId: department.id, canDelete: false }], lines: [{ entityId: line.id, canDelete: false }] };
    else if (pathname.endsWith('/api/factories')) data = { items: [factory] };
    else if (pathname.endsWith('/api/departments')) data = { items: [department] };
    else if (pathname.endsWith('/api/production-lines')) data = { items: [line] };
    else if (pathname.includes('/api/departments/') && pathname.endsWith('/main-stages')) data = { items: [{ id: 'main-stage-1', departmentId: department.id, name: 'المرحلة الرئيسية', sequenceOrder: 1, isCritical: false, isActive: true }] };
    else if (pathname.includes('/api/main-stages/') && pathname.endsWith('/sub-stages')) data = { items: [{ id: subStageIds[0], mainStageId: 'main-stage-1', mainStageName: 'المرحلة الرئيسية', factoryId: factory.id, departmentId: department.id, name: stages[0].stageName, code: stages[0].stageCode, capacity: 6, defaultOrder: 1, isActive: true }] };
    else if (pathname.endsWith('/api/product-models')) data = { items: [model], totalCount: 1, pageNumber: 1, pageSize: 50 };
    else if (pathname.endsWith('/api/production/lookups/models')) data = { items: [model] };
    else if (pathname.endsWith('/api/production/orders') || pathname.endsWith('/api/production/records')) data = [];
    else if (pathname.endsWith('/api/production/reports/daily')) data = [];
    else if (pathname.endsWith('/api/attendance/daily-summary')) data = { date: '2026-07-21', items: [] };

    await route.fulfill({ status: 200, contentType: 'application/json', body: response(data) });
  });
  return diagnostics;
}

function expectCleanDiagnostics(diagnostics: PageDiagnostics): void {
  expect(diagnostics.consoleErrors).toEqual([]);
  expect(diagnostics.failedRequests).toEqual([]);
  expect(diagnostics.errorResponses).toEqual([]);
}

async function expectViewportSafe(page: Page): Promise<void> {
  await expect(page.locator('.p-dialog.ng-animating')).toHaveCount(0);
  const geometry = await page.evaluate(() => {
    const root = document.documentElement;
    const main = document.querySelector('.plp-app-shell__main') as HTMLElement | null;
    const activeOverlays = Array.from(document.querySelectorAll<HTMLElement>('.p-dialog, .p-overlaypanel, .p-dropdown-panel'))
      .filter(element => {
        const mask = element.classList.contains('p-dialog') ? element.closest<HTMLElement>('.p-dialog-mask') : null;
        const visibilityTarget = mask ?? element;
        return element.getAttribute('aria-hidden') !== 'true'
          && visibilityTarget.checkVisibility({ checkOpacity: true, checkVisibilityCSS: true });
      })
      .map(element => {
        const box = element.getBoundingClientRect();
        return { selector: element.className, ariaHidden: element.getAttribute('aria-hidden'), left: box.left, right: box.right, top: box.top, bottom: box.bottom };
      });
    return {
      direction: getComputedStyle(root).direction,
      rootOverflow: root.scrollWidth - root.clientWidth,
      mainOverflow: (main?.scrollWidth ?? 0) - (main?.clientWidth ?? 0),
      activeOverlays,
      viewport: { width: innerWidth, height: innerHeight }
    };
  });
  expect(geometry.direction).toBe('rtl');
  expect(geometry.rootOverflow).toBeLessThanOrEqual(1);
  expect(geometry.mainOverflow).toBeLessThanOrEqual(1);
  for (const overlay of geometry.activeOverlays) {
    const context = JSON.stringify(overlay);
    expect(overlay.left, context).toBeGreaterThanOrEqual(-1);
    expect(overlay.right, context).toBeLessThanOrEqual(geometry.viewport.width + 1);
    expect(overlay.top, context).toBeGreaterThanOrEqual(-1);
    expect(overlay.bottom, context).toBeLessThanOrEqual(geometry.viewport.height + 1);
  }
}

test('keeps departments and factory structure reusable and safe at primary device classes', async ({ page }) => {
  const diagnostics = await preparePage(page);
  for (const [name, width, height] of [
    ['desktop-1440x900', 1440, 900], ['tablet-landscape-1280x800', 1280, 800],
    ['tablet-portrait-800x1280', 800, 1280], ['mobile-390x844', 390, 844]
  ] as const) {
    await page.setViewportSize({ width, height });
    await page.goto('/manufacturing/departments');
    await expect(page.getByRole('heading', { name: 'الأقسام التشغيلية' })).toBeVisible();
    await expect(page.getByPlaceholder('ابحث باسم القسم أو الكود')).toBeVisible();
    await expect(page.getByText(department.nameAr)).toBeVisible();
    await expectViewportSafe(page);
    await page.screenshot({ path: path.join(visualOutput, `departments-${name}.png`), fullPage: true });

    await page.goto('/manufacturing/factory-structure');
    await expect(page.getByRole('heading', { name: 'بنية المصنع' })).toBeVisible();
    await expect(page.getByText(factory.name).first()).toBeVisible();
    const addFactory = page.getByRole('button', { name: 'إضافة مصنع' });
    await addFactory.click();
    await expect(page.getByRole('textbox', { name: 'اسم المصنع', exact: true })).toBeVisible();
    await page.getByRole('button', { name: 'إلغاء', exact: true }).click();
    await page.locator('.factory-structure-page__tree-shell .p-tree-toggler').first().click();
    await page.locator('.factory-structure-page__tree-shell .p-tree-toggler').nth(1).click();
    const treeLayout = await page.locator('.factory-structure-node').evaluateAll(nodes => nodes.map(node => {
      const nodeBox = node.getBoundingClientRect();
      const iconBox = node.querySelector<HTMLElement>('.factory-structure-node__type-icon')!.getBoundingClientRect();
      const menuBox = node.querySelector<HTMLElement>('.factory-structure-node__menu')!.getBoundingClientRect();
      return { type: Array.from(node.classList).find(className => className.startsWith('factory-structure-node--')) ?? '', nodeLeft: nodeBox.left, nodeRight: nodeBox.right, iconRight: iconBox.right, menuLeft: menuBox.left, menuRight: menuBox.right };
    }));
    expect(treeLayout).toHaveLength(3);
    expect(treeLayout[1].iconRight).toBeLessThan(treeLayout[0].iconRight);
    expect(treeLayout[2].iconRight).toBeLessThan(treeLayout[1].iconRight);
    expect(treeLayout[0].menuLeft).toBeCloseTo(treeLayout[1].menuLeft, 0);
    expect(treeLayout[1].menuLeft).toBeCloseTo(treeLayout[2].menuLeft, 0);
    expect(treeLayout.every(row => row.menuLeft >= row.nodeLeft && row.menuRight <= row.nodeRight)).toBeTruthy();
    await page.locator('.factory-structure-node__menu').first().click();
    await expect(page.getByText('إضافة قسم', { exact: true })).toBeVisible();
    await expectViewportSafe(page);
    await page.screenshot({ path: path.join(visualOutput, `factory-structure-${name}.png`), fullPage: true });
  }
  expectCleanDiagnostics(diagnostics);
});

test('loads the permanent staffing workspace and keeps its dialog within tablet and mobile bounds', async ({ page }) => {
  test.setTimeout(180_000);
  const diagnostics = await preparePage(page);
  for (const [name, width, height] of [
    ['desktop-1440x900', 1440, 900], ['tablet-landscape-1280x800', 1280, 800],
    ['tablet-portrait-800x1280', 800, 1280], ['mobile-390x844', 390, 844]
  ] as const) {
    await page.setViewportSize({ width, height });
    await page.goto('/manufacturing/line-staffing');
    await page.waitForLoadState('networkidle');
    const structureTrigger = page.getByRole('button', { name: /اختر من شجرة المصنع/ });
    await expect(structureTrigger).toBeEnabled();
    await structureTrigger.click();
    const structureTree = page.locator('.structure-selector__overlay .factory-structure-tree-view');
    const factoryNode = structureTree.getByText(factory.name, { exact: true })
      .locator('xpath=ancestor::*[contains(@class,"p-treenode-content")][1]');
    await factoryNode.locator('.p-tree-toggler').click({ force: true });
    const departmentNode = structureTree.getByText(department.nameAr, { exact: true })
      .locator('xpath=ancestor::*[contains(@class,"p-treenode-content")][1]');
    await departmentNode.locator('.p-tree-toggler').click({ force: true });
    await structureTree.getByText(line.name, { exact: true }).click();
    const modelSelect = page.getByLabel('الموديل').filter({ visible: true }).and(page.locator('select'));
    await expect(modelSelect).toBeEnabled();
    await modelSelect.selectOption(model.id);
    await page.getByRole('button', { name: 'تحميل مراحل الموديل' }).click();
    await expect(page.getByText('ملخص تسكين الموديل والخط')).toBeVisible();
    await expect(page.getByText(stages[0].stageName).first()).toBeVisible();
    await expectViewportSafe(page);
    await page.screenshot({ path: path.join(visualOutput, `line-staffing-${name}.png`) });

    if (name === 'tablet-portrait-800x1280' || name === 'mobile-390x844') {
      await page.getByRole('button', { name: 'إضافة إلى المرحلة' }).click();
      const dialog = page.getByRole('dialog');
      await expect(dialog).toBeVisible();
      await expect(dialog.locator('.plp-responsive-entity-row__title').filter({ hasText: workers[0].fullName })).toBeVisible();
      const header = dialog.locator('.p-dialog-header');
      const headerBox = await header.boundingBox();
      expect(headerBox?.height ?? Number.POSITIVE_INFINITY).toBeLessThan(name === 'mobile-390x844' ? 112 : 88);

      const dialogFilters = dialog.locator('.line-staffing-page__dialog-filters label');
      await expect(dialogFilters).toHaveCount(3);
      const filterBoxes = await dialogFilters.evaluateAll(elements => elements.map(element => {
        const box = element.getBoundingClientRect();
        return { top: box.top, bottom: box.bottom, width: box.width };
      }));
      if (name === 'tablet-portrait-800x1280') {
        expect(Math.max(...filterBoxes.map(box => box.bottom)) - Math.min(...filterBoxes.map(box => box.bottom))).toBeLessThanOrEqual(2);
      } else {
        expect(filterBoxes[1].top).toBeGreaterThan(filterBoxes[0].bottom - 1);
        expect(filterBoxes[2].top).toBeGreaterThan(filterBoxes[1].bottom - 1);
      }

      const currentWorkerCard = dialog.locator('plp-worker-assignment-card').filter({ hasText: workers[0].fullName });
      const otherLineWorkerCard = dialog.locator('plp-worker-assignment-card').filter({ hasText: workers[1].fullName });
      const unassignedWorkerCard = dialog.locator('plp-worker-assignment-card').filter({ hasText: workers[2].fullName });
      await expect(currentWorkerCard.locator('input[type="checkbox"]')).toBeChecked();
      await expect(otherLineWorkerCard.locator('input[type="checkbox"]')).not.toBeChecked();
      await expect(otherLineWorkerCard).toContainText(otherLine.name);
      await expect(otherLineWorkerCard).toContainText('مرحلة التشغيل الثانية');
      await expect(otherLineWorkerCard).not.toContainText(line.name);
      await expect(unassignedWorkerCard).toContainText('غير مسكن');
      await expect(unassignedWorkerCard).toContainText('لا يوجد تسكين حالي');
      await expect(unassignedWorkerCard).not.toContainText(line.name);
      await expect(unassignedWorkerCard).not.toContainText('عدد المراحل: 0');

      const lineFilter = dialog.getByLabel('خط التسكين');
      await lineFilter.selectOption(otherLine.id);
      await expect(otherLineWorkerCard).toBeVisible();
      await expect(currentWorkerCard).toBeHidden();
      await lineFilter.selectOption('unassigned');
      await expect(unassignedWorkerCard).toBeVisible();
      await expect(otherLineWorkerCard).toBeHidden();
      await lineFilter.selectOption('all');

      const candidateList = dialog.locator('.line-staffing-page__candidate-list');
      const scrollGeometry = await candidateList.evaluate(element => ({
        clientHeight: element.clientHeight,
        scrollHeight: element.scrollHeight,
        overflowY: getComputedStyle(element).overflowY,
      }));
      expect(scrollGeometry.overflowY).toBe('auto');
      expect(scrollGeometry.scrollHeight).toBeGreaterThan(scrollGeometry.clientHeight);
      await candidateList.evaluate(element => { element.scrollTop = element.scrollHeight; });
      const lastCard = dialog.locator('plp-worker-assignment-card').last();
      const [listBox, lastCardBox, footerBox] = await Promise.all([
        candidateList.boundingBox(),
        lastCard.boundingBox(),
        dialog.locator('.p-dialog-footer').boundingBox(),
      ]);
      expect(lastCardBox ? lastCardBox.y + lastCardBox.height : Number.POSITIVE_INFINITY)
        .toBeLessThanOrEqual(listBox ? listBox.y + listBox.height + 1 : 0);
      expect(listBox ? listBox.y + listBox.height : Number.POSITIVE_INFINITY)
        .toBeLessThanOrEqual((footerBox?.y ?? 0) + 1);
      await candidateList.evaluate(element => { element.scrollTop = 0; });
      await expectViewportSafe(page);
      await page.screenshot({ path: path.join(visualOutput, `line-staffing-dialog-${name}.png`) });
      await page.getByRole('button', { name: 'إلغاء', exact: true }).click();
    }
  }
  expectCleanDiagnostics(diagnostics);
});

test('redirects the legacy production-recording route safely to daily operations', async ({ page }) => {
  const diagnostics = await preparePage(page);
  await page.goto('/manufacturing/production-recording');
  await expect(page).toHaveURL(/\/manufacturing\/daily-production-operations$/);
  await expect(page.getByRole('heading', { name: 'تشغيل الإنتاج اليومي', exact: true })).toBeVisible();

  for (const [name, width, height] of exactViewports) {
    await page.setViewportSize({ width, height });
    await expect(page.getByRole('heading', { name: 'تشغيل الإنتاج اليومي', exact: true })).toBeVisible();
    await expectViewportSafe(page);
    await page.screenshot({ path: path.join(visualOutput, `daily-production-redirect-${name}.png`), fullPage: true });
  }
  expectCleanDiagnostics(diagnostics);
});
