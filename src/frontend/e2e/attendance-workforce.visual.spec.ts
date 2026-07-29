import { expect, Page, test } from '@playwright/test';
import { mkdir } from 'node:fs/promises';
import path from 'node:path';

const visualOutput = path.join(process.cwd(), 'test-results', 'attendance-workforce');
const permissions = ['dashboard.view', 'attendance.view', 'attendance.sync', 'assignments.view', 'factories.view', 'production-lines.view', 'stages.view'];

const assignment = { assignmentId: 'assignment-1', assignmentType: 'Default', subStageId: 'sub-1', mainStageId: 'main-1', productionLineId: 'line-1', factoryId: 'factory-1', factoryName: 'شالنجر', productionLineName: 'خط الخياطة', mainStageName: 'الخياطة', subStageName: 'علام وصلة اللسان', startsAtUtc: null, endsAtUtc: null, reason: null };
const row = { workerId: 'worker-1', employeeCode: '1001', fullName: 'فاطمة عربي', departmentName: 'الخياطة', photoReference: null, hasPhoto: false, attendanceStatus: 'Absent', firstCheckInUtc: null, lastCheckOutUtc: null, hasAttendanceData: true, hasSinglePunch: false, assignments: [assignment], isAssigned: true, hasTemporaryAssignment: false, needsReview: true };
const workforcePage = { productionDate: '2026-07-19', items: [row], summary: { totalWorkers: 1, presentWorkers: 0, absentWorkers: 1, lateWorkers: 0, incompleteWorkers: 0, unassignedPresentWorkers: 0, assignedAbsentWorkers: 1, reviewRequiredWorkers: 1, attendanceDataAvailable: true, scope: 'filtered-results' }, page: 1, pageSize: 25, totalCount: 1, totalPages: 1 };
type Diagnostics = { consoleErrors: string[]; failedRequests: string[]; unexpectedResponses: string[] };
type WorkforceMock = { payload?: typeof workforcePage; status?: number; delayMs?: number; responseGate?: Promise<void>; waitForTable?: boolean };

test.beforeAll(async () => { await mkdir(visualOutput, { recursive: true }); });

async function prepareWorkforce(page: Page, requests: { workforce: number; details: number }, mock: WorkforceMock = {}): Promise<void> {
  await page.addInitScript(({ userPermissions }) => {
    localStorage.setItem('plp.accessToken', 'attendance-workforce-visual-token');
    localStorage.setItem('plp.currentUser', JSON.stringify({ id: 'visual-user', fullName: 'مراجع الواجهة', email: 'visual@local.test', roles: ['Administrator'], permissions: userPermissions }));
  }, { userPermissions: permissions });
  await page.routeWebSocket('**/hubs/notifications**', socket => {
    socket.onMessage(message => {
      if (typeof message === 'string' && message.includes('"protocol"')) {
        socket.send('{}\u001e');
      }
    });
  });
  await page.route('**/hubs/notifications/negotiate**', route => route.fulfill({
    status: 200,
    contentType: 'application/json',
    body: JSON.stringify({
      negotiateVersion: 1,
      connectionId: 'attendance-workforce-visual',
      connectionToken: 'attendance-workforce-visual',
      availableTransports: [{ transport: 'WebSockets', transferFormats: ['Text', 'Binary'] }]
    })
  }));
  await page.route('**/api/**', async route => {
    const url = new URL(route.request().url());
    const pathname = url.pathname;
    let data: unknown = { items: [] };
    if (pathname.endsWith('/api/auth/me')) data = { id: 'visual-user', fullName: 'مراجع الواجهة', email: 'visual@local.test', roles: ['Administrator'], permissions };
    else if (pathname.endsWith('/api/attendance/workforce')) {
      requests.workforce += 1;
      if (mock.delayMs) await new Promise(resolve => setTimeout(resolve, mock.delayMs));
      if (mock.responseGate) await mock.responseGate;
      if (mock.status && mock.status >= 400) {
        await route.fulfill({ status: mock.status, contentType: 'application/json', body: JSON.stringify({ success: false, data: null, error: { message: 'تعذر تحميل البيانات للاختبار.' } }) });
        return;
      }
      data = mock.payload ?? workforcePage;
    }
    else if (pathname.endsWith('/api/attendance/workforce/workers/worker-1/details')) { requests.details += 1; data = { workerId: 'worker-1', productionDate: '2026-07-19', attendanceRecords: [], assignments: [assignment] }; }
    else if (pathname.endsWith('/api/factories')) data = { items: [{ id: 'factory-1', name: 'شالنجر', code: 'CH', isActive: true }] };
    else if (pathname.endsWith('/api/production-lines')) data = { items: [{ id: 'line-1', factoryId: 'factory-1', departmentId: 'department-1', name: 'خط الخياطة', sequenceOrder: 1, isActive: true }] };
    else if (pathname.includes('/main-stages')) data = { items: [{ id: 'main-1', departmentId: 'department-1', name: 'الخياطة', sequenceOrder: 1, isCritical: false, isActive: true }] };
    else if (pathname.includes('/sub-stages')) data = { items: [{ id: 'sub-1', mainStageId: 'main-1', name: 'علام وصلة اللسان', code: 'SUB-1', capacity: 1, defaultOrder: 1, isActive: true }] };
    else if (pathname.includes('/sync/production-date/')) data = { syncDateUtc: '2026-07-19T00:00:00Z', sourceUsersCount: 1, sourceCheckInsCount: 1, matchedWorkersCount: 1, unmatchedSourceUsersCount: 0, workersWithoutAttendanceCount: 0, insertedRecords: 1, updatedRecords: 0, skippedRecords: 0 };
    await route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify({ success: true, data, error: null }) });
  });
}

async function openWorkforce(page: Page, requests: { workforce: number; details: number }, mock: WorkforceMock = {}): Promise<void> {
  await prepareWorkforce(page, requests, mock);
  await page.goto('/attendance/workforce', { waitUntil: mock.waitForTable === false ? 'commit' : 'load' });
  if (mock.waitForTable !== false) await expect(page.locator('.workforce-page__table')).toBeVisible();
}

function collectDiagnostics(page: Page): Diagnostics {
  const diagnostics: Diagnostics = { consoleErrors: [], failedRequests: [], unexpectedResponses: [] };
  page.on('console', message => { if (message.type() === 'error') diagnostics.consoleErrors.push(message.text()); });
  page.on('pageerror', error => diagnostics.consoleErrors.push(error.message));
  page.on('requestfailed', request => diagnostics.failedRequests.push(`${request.method()} ${request.url()} ${request.failure()?.errorText ?? ''}`));
  page.on('response', response => { if (response.status() >= 400) diagnostics.unexpectedResponses.push(`${response.status()} ${response.url()}`); });
  return diagnostics;
}

async function expectViewportSafe(page: Page): Promise<void> {
  const dimensions = await page.evaluate(() => ({ scrollWidth: document.documentElement.scrollWidth, clientWidth: document.documentElement.clientWidth }));
  expect(dimensions.scrollWidth).toBeLessThanOrEqual(dimensions.clientWidth + 1);
  await expect(page.locator('.workforce-page__expand-button')).toBeVisible();
}

test('renders the workforce workspace safely across desktop, tablet, and phone viewports', async ({ page }) => {
  const requests = { workforce: 0, details: 0 };
  const diagnostics = collectDiagnostics(page);
  const viewports = [
    ['desktop-1440x900', 1440, 900], ['desktop-1280x800', 1280, 800],
    ['tablet-landscape-1280x800', 1280, 800], ['tablet-landscape-1024x768', 1024, 768],
    ['tablet-portrait-800x1280', 800, 1280], ['tablet-portrait-768x1024', 768, 1024],
    ['mobile-430x932', 430, 932], ['mobile-390x844', 390, 844], ['mobile-360x800', 360, 800]
  ] as const;
  for (const [name, width, height] of viewports) {
    await page.setViewportSize({ width, height });
    await openWorkforce(page, requests);
    await expectViewportSafe(page);
    await page.screenshot({ path: path.join(visualOutput, `${name}.png`), fullPage: true });
  }
  expect(requests.workforce).toBe(viewports.length);
  expect(diagnostics.consoleErrors).toEqual([]);
  expect(diagnostics.failedRequests).toEqual([]);
  expect(diagnostics.unexpectedResponses).toEqual([]);
});

test('uses the compact filter panel without reloading and loads one worker detail on demand', async ({ page }) => {
  const requests = { workforce: 0, details: 0 };
  const diagnostics = collectDiagnostics(page);
  await page.setViewportSize({ width: 390, height: 844 });
  await openWorkforce(page, requests);
  const beforeToggle = requests.workforce;
  await page.getByRole('button', { name: /إظهار الفلاتر/ }).click();
  await expect(page.locator('.workforce-page__filters')).toBeVisible();
  expect(requests.workforce).toBe(beforeToggle);
  await page.getByRole('button', { name: 'عرض التفاصيل' }).click();
  await expect(page.getByRole('heading', { name: 'التسكينات الفعالة' })).toBeVisible();
  expect(requests.details).toBe(1);
  await page.screenshot({ path: path.join(visualOutput, 'mobile-expanded-390x844.png'), fullPage: true });
  expect(diagnostics.consoleErrors).toEqual([]);
  expect(diagnostics.failedRequests).toEqual([]);
  expect(diagnostics.unexpectedResponses).toEqual([]);
});

test('keeps loading, empty, and retry states usable on a phone viewport', async ({ page }) => {
  const requests = { workforce: 0, details: 0 };
  await page.setViewportSize({ width: 390, height: 844 });
  let releaseResponse: () => void = () => undefined;
  const responseGate = new Promise<void>(resolve => { releaseResponse = resolve; });
  await prepareWorkforce(page, requests, { responseGate });
  const navigation = page.goto('/attendance/workforce');
  const skeleton = page.locator('.p-skeleton').first();
  await expect(skeleton).toHaveCount(1);
  const skeletonMetrics = await skeleton.evaluate(element => {
    const style = getComputedStyle(element);
    const box = element.getBoundingClientRect();
    const parentBox = element.parentElement?.getBoundingClientRect();
    return { display: style.display, visibility: style.visibility, opacity: style.opacity, width: box.width, height: box.height, parentWidth: parentBox?.width ?? 0, parentHeight: parentBox?.height ?? 0 };
  });
  expect(skeletonMetrics).toMatchObject({ display: 'block', visibility: 'visible', opacity: '1' });
  expect(skeletonMetrics.width).toBeGreaterThan(0);
  expect(skeletonMetrics.height).toBeGreaterThan(0);
  await page.waitForTimeout(100);
  await page.screenshot({ path: path.join(visualOutput, 'mobile-loading-390x844.png'), fullPage: true });
  releaseResponse();
  await navigation;
  await expect(page.locator('.workforce-page__table')).toBeVisible();
});

test('keeps empty and error states actionable on a phone viewport', async ({ page }) => {
  const emptyRequests = { workforce: 0, details: 0 };
  await page.setViewportSize({ width: 390, height: 844 });
  await openWorkforce(page, emptyRequests, { payload: { ...workforcePage, items: [], totalCount: 0, totalPages: 0, summary: { ...workforcePage.summary, totalWorkers: 0, presentWorkers: 0, absentWorkers: 0, lateWorkers: 0, incompleteWorkers: 0, unassignedPresentWorkers: 0, assignedAbsentWorkers: 0, reviewRequiredWorkers: 0 } }, waitForTable: false });
  await expect(page.getByText('لا توجد نتائج')).toBeVisible();
  await expect(page.getByRole('button', { name: 'إعادة الضبط' })).toBeVisible();
  await page.screenshot({ path: path.join(visualOutput, 'mobile-empty-390x844.png'), fullPage: true });

  const errorRequests = { workforce: 0, details: 0 };
  await page.unrouteAll({ behavior: 'wait' });
  await openWorkforce(page, errorRequests, { status: 500, waitForTable: false });
  await expect(page.locator('section[role="alert"]')).toBeVisible();
  await expect(page.getByRole('button', { name: 'إعادة المحاولة' })).toBeVisible();
  await page.screenshot({ path: path.join(visualOutput, 'mobile-error-390x844.png'), fullPage: true });
});
