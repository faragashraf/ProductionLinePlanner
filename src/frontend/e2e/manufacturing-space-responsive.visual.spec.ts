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
const model = { id: 'model-1', code: 'MODEL-01', name: 'موديل التشغيل التجريبي', isActive: true };
const subStageIds = [
  '11111111-1111-1111-1111-111111111111',
  '22222222-2222-2222-2222-222222222222',
  '33333333-3333-3333-3333-333333333333'
];

const workers = Array.from({ length: 4 }, (_, index) => ({
  workerId: `aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaa${index}`,
  employeeCode: `W-${String(index + 1).padStart(3, '0')}`,
  fullName: index === 0 ? 'عامل تشغيل باسم عربي طويل لاختبار الالتفاف الآمن داخل اللوحي' : `عامل التشغيل ${index + 1}`,
  departmentName: department.nameAr,
  isOnActiveService: true,
  hasPhoto: false,
  photoReference: null,
  photoVersion: null,
  defaultSubStageId: index < 2 ? subStageIds[0] : null,
  defaultSubStageName: index < 2 ? 'مرحلة القص والتجهيز الأولي' : null,
  effectiveAssignmentId: index < 2 ? `assignment-${index + 1}` : null,
  effectiveAssignmentType: index < 2 ? 'Default' : null,
  effectiveSubStageId: index < 2 ? subStageIds[0] : null,
  effectiveSubStageName: index < 2 ? 'مرحلة القص والتجهيز الأولي' : null,
  fromSubStageId: null,
  fromSubStageName: null,
  temporaryStartsAtUtc: null,
  temporaryEndsAtUtc: null,
  replacementForWorkerId: null,
  participations: index < 2 ? [{
    assignmentId: `assignment-${index + 1}`,
    assignmentType: 'Default',
    subStageId: subStageIds[0],
    subStageName: 'مرحلة القص والتجهيز الأولي',
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
  defaultAssignedWorkersCount: index === 0 ? 2 : 0,
  effectiveAssignedWorkersCount: index === 0 ? 2 : 0,
  temporaryAssignedWorkersCount: 0,
  requiredWorkers: index === 0 ? 2 : null,
  hasAuthoritativeRequiredWorkerCount: index === 0,
  staffingStatus: index === 0 ? 'Ready' : 'NeedsStaffingReview',
  workerStatusText: index === 0 ? 'التسكين مطابق للعدد المطلوب' : 'تحتاج المرحلة إلى تسكين',
  effectiveWorkerIds: index === 0 ? workers.slice(0, 2).map(worker => worker.workerId) : []
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
    else if (pathname.endsWith('/api/factories')) data = { items: [factory] };
    else if (pathname.endsWith('/api/departments')) data = { items: [department] };
    else if (pathname.endsWith('/api/production-lines')) data = { items: [line] };
    else if (pathname.includes('/api/production-lines/') && pathname.endsWith('/main-stages')) data = { items: [{ id: 'main-stage-1', productionLineId: line.id, name: 'المرحلة الرئيسية', sequenceOrder: 1, isCritical: false, isActive: true }] };
    else if (pathname.includes('/api/main-stages/') && pathname.endsWith('/sub-stages')) data = { items: [{ id: subStageIds[0], mainStageId: 'main-stage-1', productionLineId: line.id, factoryId: factory.id, departmentId: department.id, name: stages[0].stageName, code: stages[0].stageCode, capacity: 6, defaultOrder: 1, isActive: true }] };
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
    await expectViewportSafe(page);
    await page.screenshot({ path: path.join(visualOutput, `factory-structure-${name}.png`), fullPage: true });
  }
  expectCleanDiagnostics(diagnostics);
});

test('loads the permanent staffing workspace and keeps its dialog within tablet and mobile bounds', async ({ page }) => {
  const diagnostics = await preparePage(page);
  for (const [name, width, height] of [
    ['desktop-1440x900', 1440, 900], ['tablet-landscape-960x600', 960, 600],
    ['tablet-portrait-800x1280', 800, 1280], ['mobile-390x844', 390, 844]
  ] as const) {
    await page.setViewportSize({ width, height });
    await page.goto('/manufacturing/line-staffing');
    const context = page.locator('.line-staffing-page__context');
    await context.locator('select').nth(0).selectOption(factory.id);
    await context.locator('select').nth(1).selectOption(department.id);
    await context.locator('select').nth(2).selectOption(line.id);
    await context.locator('select').nth(3).selectOption(model.id);
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
      await expectViewportSafe(page);
      await page.screenshot({ path: path.join(visualOutput, `line-staffing-dialog-${name}.png`) });
      await page.getByRole('button', { name: 'إلغاء', exact: true }).click();
    }
  }
  expectCleanDiagnostics(diagnostics);
});

test('keeps production recording safe at every required and intermediate viewport without reload on rotation', async ({ page }) => {
  const diagnostics = await preparePage(page);
  await page.goto('/manufacturing/production-recording');
  await expect(page.getByRole('heading', { name: 'تشغيل وتسجيل الإنتاج' })).toBeVisible();

  for (const [name, width, height] of exactViewports) {
    await page.setViewportSize({ width, height });
    await expect(page.getByRole('heading', { name: 'تشغيل وتسجيل الإنتاج' })).toBeVisible();
    await expectViewportSafe(page);
    const hierarchy = page.locator('.production-page__hierarchy');
    const columnCount = await hierarchy.evaluate(element => getComputedStyle(element).gridTemplateColumns.split(' ').length);
    expect(columnCount).toBe(width < 600 ? 1 : width < 1024 ? 2 : 4);
    const minimumControlHeight = await hierarchy.locator('select').first().evaluate(element => element.getBoundingClientRect().height);
    expect(minimumControlHeight).toBeGreaterThanOrEqual(43);
    await page.screenshot({ path: path.join(visualOutput, `production-recording-${name}.png`), fullPage: true });
  }
  expectCleanDiagnostics(diagnostics);
});
