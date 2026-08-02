import { expect, Page, test } from '@playwright/test';
import { mkdir } from 'node:fs/promises';
import path from 'node:path';

const visualOutput = path.join(process.cwd(), 'test-results', 'worker-management');
const workerId = '11111111-1111-1111-1111-111111111111';
const managerPermissions = [
  'workers.view', 'workers.manage', 'assignments.view', 'attendance.view', 'compensation.view', 'departments.manage'
];
const png = Buffer.from('iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAusB9Y9ZsFQAAAAASUVORK5CYII=', 'base64');

type Scenario = 'default' | 'loading' | 'empty' | 'error';
interface ScenarioState { current: Scenario; }

const worker = (hasPhoto = false) => ({
  id: workerId,
  employeeCode: 'EMP-101',
  fullName: 'فاطمة أحمد عبد الرحمن',
  phone: '01000000000',
  localDepartmentName: 'تشغيل',
  attendanceUserId: '101',
  attendanceDepartmentId: 7,
  badgeNumber: 'B-101',
  isActive: true,
  employmentStatus: 'Active',
  employmentEndDate: null,
  defaultSubStageId: '44444444-4444-4444-4444-444444444444',
  organizationalDepartmentId: '55555555-5555-5555-5555-555555555555',
  organizationalDepartmentName: 'قسم التشغيل',
  organizationalFactoryId: '66666666-6666-6666-6666-666666666666',
  organizationalFactoryName: 'المصنع الرئيسي',
  organizationalDepartmentConcurrencyToken: 'worker-concurrency-token',
  lastExternalSyncAt: '2026-07-29T07:00:00Z',
  createdAtUtc: '2026-01-01T08:00:00Z',
  updatedAtUtc: '2026-07-29T07:00:00Z',
  permanentAssignments: [{
    id: '77777777-7777-7777-7777-777777777777',
    factoryId: '66666666-6666-6666-6666-666666666666',
    factoryName: 'المصنع الرئيسي',
    productionLineId: '88888888-8888-8888-8888-888888888888',
    productionLineName: 'خط التجهيز',
    departmentId: '55555555-5555-5555-5555-555555555555',
    departmentName: 'قسم التشغيل',
    mainStageId: '99999999-9999-9999-9999-999999999999',
    mainStageName: 'التجهيز',
    subStageId: '44444444-4444-4444-4444-444444444444',
    subStageName: 'القص',
    assignedAtUtc: '2026-07-01T08:00:00Z'
  }],
  hasPhoto,
  photoReference: hasPhoto ? `/api/workers/${workerId}/photo?v=${'b'.repeat(64)}` : null,
  photoVersion: hasPhoto ? 'b'.repeat(64) : null
});

test.beforeAll(async () => { await mkdir(visualOutput, { recursive: true }); });

async function prepareWorkspace(
  page: Page,
  state: ScenarioState,
  permissions = managerPermissions
): Promise<{ consoleErrors: string[]; failedRequests: string[] }> {
  let hasPhoto = false;
  const consoleErrors: string[] = [];
  const failedRequests: string[] = [];
  page.on('console', message => { if (message.type() === 'error') consoleErrors.push(message.text()); });
  page.on('requestfailed', request => failedRequests.push(`${request.method()} ${request.url()}`));

  await page.routeWebSocket('**/hubs/notifications**', socket => {
    socket.onMessage(message => {
      if (typeof message === 'string' && message.includes('"protocol"')) socket.send('{}\u001e');
    });
  });
  await page.route('**/hubs/notifications/negotiate**', route => route.fulfill({
    status: 200,
    contentType: 'application/json',
    body: JSON.stringify({ negotiateVersion: 1, connectionId: 'worker-management-visual', connectionToken: 'worker-management-visual', availableTransports: [{ transport: 'WebSockets', transferFormats: ['Text', 'Binary'] }] })
  }));

  await page.route('**/api/**', async route => {
    const request = route.request();
    const pathname = new URL(request.url()).pathname;
    if (pathname === '/api/auth/login' && request.method() === 'POST') {
      await route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify({
        success: true,
        data: { accessToken: `visual-${crypto.randomUUID()}`, refreshToken: null, expiresAt: '2099-01-01T00:00:00Z', userId: 'visual-worker-manager', roles: ['Administrator'], permissions },
        error: null
      }) });
      return;
    }
    if (pathname === '/api/auth/me') {
      await route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify({ success: true, data: { id: 'visual-worker-manager', fullName: 'مراجع إدارة العاملين', email: 'visual@test.invalid', roles: ['Administrator'], permissions }, error: null }) });
      return;
    }
    if (pathname === '/api/workers' && request.method() === 'GET') {
      if (state.current === 'loading') await new Promise(resolve => setTimeout(resolve, 1000));
      if (state.current === 'error') {
        await route.fulfill({ status: 500, contentType: 'application/json', body: JSON.stringify({ success: false, data: null, error: { message: 'تعذر تحميل العاملين.' } }) });
        return;
      }
      const visibleWorker = permissions.includes('assignments.view')
        ? worker(hasPhoto)
        : { ...worker(hasPhoto), defaultSubStageId: null, permanentAssignments: [] };
      const items = state.current === 'empty' ? [] : [visibleWorker];
      await route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify({ success: true, data: { items, totalCount: items.length, pageNumber: 1, pageSize: 6 }, error: null }) });
      return;
    }
    if (pathname === `/api/workers/${workerId}` && request.method() === 'GET') {
      const visibleWorker = permissions.includes('assignments.view')
        ? worker(hasPhoto)
        : { ...worker(hasPhoto), defaultSubStageId: null, permanentAssignments: [] };
      await route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify({ success: true, data: visibleWorker, error: null }) });
      return;
    }
    if (pathname === `/api/workers/${workerId}` && request.method() === 'PATCH') {
      const body = request.postDataJSON() as { fullName?: string };
      await route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify({ success: true, data: { ...worker(hasPhoto), fullName: body.fullName ?? worker(hasPhoto).fullName }, error: null }) });
      return;
    }
    if (pathname === `/api/workers/${workerId}/employment-status`) {
      await route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify({ success: true, data: { ...worker(hasPhoto), employmentStatus: 'Suspended', isActive: false }, error: null }) });
      return;
    }
    if (pathname === `/api/workers/${workerId}/compensation/current`) {
      await route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify({ success: true, data: { id: 'salary-1', workerId, amount: 7250, currencyCode: 'EGP', effectiveFrom: '2026-07-01T00:00:00Z', effectiveTo: null }, error: null }) });
      return;
    }
    if (pathname === `/api/attendance/workforce/workers/${workerId}/summary`) {
      await route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify({ success: true, data: { workerId, productionDate: '2026-07-29', todayStatus: 'Present', attendanceDataAvailableForDate: true, firstCheckInUtc: '2026-07-29T05:02:00Z', lastCheckOutUtc: '2026-07-29T14:10:00Z', lastKnownMovementUtc: '2026-07-29T14:10:00Z' }, error: null }) });
      return;
    }
    if (pathname === `/api/workers/${workerId}/attendance-records`) {
      const query = new URL(request.url()).searchParams;
      const pageNumber = Number(query.get('page') ?? 1);
      const inUtc = pageNumber === 1 ? '2026-07-29T05:02:00Z' : '2026-07-18T05:05:00Z';
      const outUtc = pageNumber === 1 ? '2026-07-29T14:10:00Z' : '2026-07-18T14:00:00Z';
      await route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify({ success: true, data: {
        workerId, fromDate: query.get('fromDate'), toDate: query.get('toDate'), page: pageNumber, pageSize: 10, totalCount: 11, totalPages: 2,
        items: [{ recordId: `attendance-record-${pageNumber}`, productionDate: pageNumber === 1 ? '2026-07-29' : '2026-07-18', attendanceStatus: 'Present', source: 'AttendanceSync', movements: [
          { occurredAtUtc: inUtc, movementType: 'In' },
          { occurredAtUtc: outUtc, movementType: 'Out' }
        ] }]
      }, error: null }) });
      return;
    }
    if (pathname === '/api/factories' && request.method() === 'GET') {
      await route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify({ success: true, data: { items: [
        { id: '66666666-6666-6666-6666-666666666666', name: 'المصنع الرئيسي', code: 'F-1', isActive: true }
      ] }, error: null }) });
      return;
    }
    if (pathname === '/api/departments' && request.method() === 'GET') {
      await route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify({ success: true, data: { items: [
        { id: '55555555-5555-5555-5555-555555555555', factoryId: '66666666-6666-6666-6666-666666666666', code: 'D-1', nameAr: 'قسم التشغيل', isActive: true },
        { id: 'aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa', factoryId: '66666666-6666-6666-6666-666666666666', code: 'D-2', nameAr: 'قسم التعبئة', isActive: true }
      ] }, error: null }) });
      return;
    }
    if (pathname === `/api/workers/${workerId}/organizational-department` && request.method() === 'PUT') {
      await route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify({ success: true, data: {
        workerId, departmentId: 'aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa', departmentName: 'قسم التعبئة',
        factoryId: '66666666-6666-6666-6666-666666666666', factoryName: 'المصنع الرئيسي', concurrencyToken: 'updated-worker-token', updatedAtUtc: '2026-07-29T10:00:00Z'
      }, error: null }) });
      return;
    }
    if (pathname === `/api/workers/${workerId}/photo` && request.method() === 'PUT') {
      hasPhoto = true;
      await route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify({ success: true, data: { photo: { workerId, photoReference: `/api/workers/${workerId}/photo?v=${'b'.repeat(64)}`, version: 'b'.repeat(64), contentType: 'image/png', length: png.length }, created: true, replaced: false, unchanged: false }, error: null }) });
      return;
    }
    if (pathname === `/api/workers/${workerId}/photo` && request.method() === 'DELETE') {
      hasPhoto = false;
      await route.fulfill({ status: 204 });
      return;
    }
    if (pathname === `/api/workers/${workerId}/photo` && request.method() === 'GET') {
      await route.fulfill({ status: 200, contentType: 'image/png', body: png });
      return;
    }
    await route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify({ success: true, data: { items: [] }, error: null }) });
  });

  // Exercise the real login UI with per-run generated values; no credentials or session are injected or persisted by the test.
  await page.goto('/login');
  await page.getByRole('textbox', { name: 'اسم المستخدم' }).fill(`visual-${crypto.randomUUID()}@test.invalid`);
  await page.getByRole('textbox', { name: 'كلمة المرور' }).fill(`Visual-${crypto.randomUUID()}!`);
  await page.getByRole('button', { name: 'دخول إلى مساحة العمل' }).click();
  await page.waitForURL('**/dashboard');
  await page.goto('/workers');
  await expect(page.getByRole('heading', { name: 'إدارة العاملين' })).toBeVisible();
  // Dashboard requests are outside this screen's route harness; start diagnostics after leaving that component.
  consoleErrors.length = 0;
  failedRequests.length = 0;
  return { consoleErrors, failedRequests };
}

async function openList(page: Page): Promise<void> {
  await page.goto('/workers');
  await expect(page.getByRole('heading', { name: 'إدارة العاملين' })).toBeVisible();
  await expect(page.locator('.worker-management-page__table')).toBeVisible();
}

async function openProfile(page: Page): Promise<void> {
  const action = page.getByRole('button', { name: 'فتح ملف العامل فاطمة أحمد عبد الرحمن' });
  if (await action.count()) {
    await action.click();
  } else {
    await page.getByRole('button', { name: 'إجراءات العامل فاطمة أحمد عبد الرحمن' }).click();
    const actionMenu = page.getByRole('menu');
    await expect(actionMenu).toBeVisible();
    const openProfileItem = actionMenu.getByRole('menuitem', { name: 'فتح الملف' });
    await expect(openProfileItem).toBeVisible();
    await openProfileItem.click();
  }
  await expect(page.locator('[data-workspace-view="profile"]')).toBeVisible();
}

async function expectViewportSafe(page: Page): Promise<void> {
  const geometry = await page.evaluate(() => ({
    direction: getComputedStyle(document.documentElement).direction,
    scrollWidth: document.documentElement.scrollWidth,
    clientWidth: document.documentElement.clientWidth
  }));
  expect(geometry.direction).toBe('rtl');
  expect(geometry.scrollWidth).toBeLessThanOrEqual(geometry.clientWidth + 1);
  const action = page.locator('.worker-management-page__action-menu-button');
  await expect(action).toHaveCount(1);
  expect((await action.boundingBox())?.height ?? 0).toBeGreaterThanOrEqual(44);
}

async function expectPhotoWorkspaceSafe(page: Page, screenshotName: string): Promise<void> {
  await openProfile(page);
  const photoArea = page.locator('.worker-profile__photo-draft');
  await expect(photoArea).toBeVisible();
  await photoArea.scrollIntoViewIfNeeded();
  const choosePhoto = page.getByText('اختيار صورة', { exact: true });
  const savePhoto = page.getByRole('button', { name: 'حفظ الصورة', exact: true });
  await expect(choosePhoto).toBeVisible();
  await expect(savePhoto).toBeVisible();
  expect((await choosePhoto.locator('..').boundingBox())?.height ?? 0).toBeGreaterThanOrEqual(44);
  expect((await savePhoto.boundingBox())?.height ?? 0).toBeGreaterThanOrEqual(44);
  const geometry = await page.evaluate(() => ({ scrollWidth: document.documentElement.scrollWidth, clientWidth: document.documentElement.clientWidth }));
  expect(geometry.scrollWidth).toBeLessThanOrEqual(geometry.clientWidth + 1);
  await page.screenshot({ path: path.join(visualOutput, `${screenshotName}-profile.png`), fullPage: true });
}

async function expectPhotoHoverPreview(page: Page, screenshotName: string, width: number, height: number): Promise<void> {
  const avatar = page.locator('.worker-profile__hero plp-worker-avatar .plp-worker-avatar');
  await expect(avatar).toHaveCount(1);
  await avatar.scrollIntoViewIfNeeded();
  await expect(avatar.locator('img')).toBeVisible();
  await avatar.hover();

  const preview = page.locator('.plp-worker-photo-preview');
  await expect(preview).toBeVisible();
  await expect(preview.locator('.plp-worker-avatar__preview img')).toBeVisible();
  const previewBox = await preview.boundingBox();
  expect(previewBox?.x ?? -1).toBeGreaterThanOrEqual(0);
  expect(previewBox?.y ?? -1).toBeGreaterThanOrEqual(0);
  expect((previewBox?.x ?? 0) + (previewBox?.width ?? 0)).toBeLessThanOrEqual(width + 1);
  expect((previewBox?.y ?? 0) + (previewBox?.height ?? 0)).toBeLessThanOrEqual(height + 1);
  expect(previewBox?.width ?? 0).toBeGreaterThanOrEqual(190);
  await page.screenshot({ path: path.join(visualOutput, `${screenshotName}-photo-hover-preview.png`), fullPage: true });

  await page.mouse.move(0, 0);
  await expect(preview).toBeHidden();
  const geometry = await page.evaluate(() => ({
    scrollWidth: document.documentElement.scrollWidth,
    clientWidth: document.documentElement.clientWidth
  }));
  expect(geometry.scrollWidth).toBeLessThanOrEqual(geometry.clientWidth + 1);
}

test('uses API-backed worker data and remains RTL/overflow safe at required viewports', async ({ page }) => {
  const state: ScenarioState = { current: 'default' };
  const diagnostics = await prepareWorkspace(page, state);
  for (const [name, width, height] of [
    ['desktop-1440x900', 1440, 900],
    ['desktop-1280x800', 1280, 800],
    ['tablet-landscape-1024x768', 1024, 768],
    ['android-tablet-portrait-800x1280', 800, 1280],
    ['tablet-portrait-768x1024', 768, 1024],
    ['mobile-390x844', 390, 844]
  ] as const) {
    await page.setViewportSize({ width, height });
    await openList(page);
    await expect(page.locator('.worker-management-page__table')).toContainText('فاطمة أحمد عبد الرحمن');
    await expect(page.locator('.worker-management-page__table')).toContainText('المصنع الرئيسي / خط التجهيز');
    await expectViewportSafe(page);
    await page.screenshot({ path: path.join(visualOutput, `${name}.png`), fullPage: true });
    if (width <= 1024) {
      await page.locator('.worker-management-page__table').screenshot({ path: path.join(visualOutput, `${name}-table.png`) });
    }
    if ([1024, 800, 768].includes(width)) {
      await expectPhotoWorkspaceSafe(page, name);
    }
  }
  expect(diagnostics.consoleErrors).toEqual([]);
  expect(diagnostics.failedRequests).toEqual([]);
});

test('searches and keeps the tablet photo preview lifecycle isolated until save', async ({ page }) => {
  const state: ScenarioState = { current: 'default' };
  const diagnostics = await prepareWorkspace(page, state);
  await page.setViewportSize({ width: 800, height: 1280 });
  await openList(page);
  await page.locator('#workerManagementSearch').fill('فاطمة');
  await expect(page.locator('.worker-management-page__table')).toContainText('فاطمة أحمد عبد الرحمن');
  await openProfile(page);
  await expect(page.getByText('JPEG وPNG وBMP فقط، بحد أقصى 5 MiB.')).toBeVisible();

  await page.locator('#workerPhotoInput').setInputFiles({ name: 'worker.png', mimeType: 'image/png', buffer: png });
  await expect(page.getByText('تم اختيار صورة جديدة')).toBeVisible();
  await expect(page.locator('img[alt="معاينة الصورة المختارة"]')).toBeVisible();
  await page.getByRole('button', { name: 'إلغاء الاختيار' }).click();
  await expect(page.locator('img[alt="معاينة الصورة المختارة"]')).toHaveCount(0);

  await page.getByRole('button', { name: 'العودة إلى قائمة العاملين' }).click();
  await openProfile(page);
  await expect(page.locator('img[alt="معاينة الصورة المختارة"]')).toHaveCount(0);
  await page.locator('#workerPhotoInput').setInputFiles({ name: 'worker.png', mimeType: 'image/png', buffer: png });
  await page.getByRole('button', { name: 'حفظ الصورة' }).click();
  await expect(page.getByText('تم حفظ الصورة المحلية وتحديثها فورًا.')).toBeVisible();
  await expect(page.getByRole('img', { name: 'صورة العامل فاطمة أحمد عبد الرحمن' })).toBeVisible();

  for (const [name, width, height] of [
    ['desktop-1440x900', 1440, 900],
    ['android-tablet-landscape-1280x800', 1280, 800],
    ['android-tablet-portrait-800x1280', 800, 1280],
    ['mobile-390x844', 390, 844]
  ] as const) {
    await page.setViewportSize({ width, height });
    await expectPhotoHoverPreview(page, name, width, height);
  }

  const geometry = await page.evaluate(() => ({ scrollWidth: document.documentElement.scrollWidth, clientWidth: document.documentElement.clientWidth }));
  expect(geometry.scrollWidth).toBeLessThanOrEqual(geometry.clientWidth + 1);
  await page.screenshot({ path: path.join(visualOutput, 'android-tablet-photo-saved-800x1280.png'), fullPage: true });
  await page.locator('[data-workspace-view="profile"]').screenshot({ path: path.join(visualOutput, 'android-tablet-profile-photo-saved-800x1280.png') });
  expect(diagnostics.consoleErrors).toEqual([]);
  expect(diagnostics.failedRequests).toEqual([]);
});

test('shows loading, empty, and error states separately', async ({ page }) => {
  const state: ScenarioState = { current: 'loading' };
  await prepareWorkspace(page, state);
  await page.setViewportSize({ width: 390, height: 844 });
  await page.goto('/workers', { waitUntil: 'commit' });
  await expect(page.locator('.plp-product-loading')).toBeVisible();

  state.current = 'empty';
  await page.goto('/workers');
  await expect(page.getByText('لا توجد نتائج')).toBeVisible();

  state.current = 'error';
  await page.goto('/workers');
  await expect(page.getByRole('button', { name: 'إعادة المحاولة' })).toBeVisible();
});

test('hides write actions and protected summaries for workers.view-only users', async ({ page }) => {
  const state: ScenarioState = { current: 'default' };
  await prepareWorkspace(page, state, ['workers.view']);
  await openList(page);
  await openProfile(page);
  await expect(page.getByText('تتطلب التعديلات والصور `workers.manage`.')).toBeVisible();
  await expect(page.getByText('غير مخول بعرض الراتب')).toBeVisible();
  await page.getByRole('button', { name: 'التسكين والتشغيل' }).click();
  await expect(page.getByText('لا تملك صلاحية عرض تفاصيل التسكين.')).toBeVisible();
  await page.getByRole('button', { name: 'الحضور' }).click();
  await expect(page.getByText('لا تملك صلاحية `attendance.view` لعرض ملخص الحضور.')).toBeVisible();
  await expect(page.getByRole('button', { name: 'حفظ البيانات المحلية' })).toHaveCount(0);
});

test('uses the real profile UI for attendance range, paging, colors and organizational department assignment on tablets', async ({ page }) => {
  test.setTimeout(60_000);
  const state: ScenarioState = { current: 'default' };
  const diagnostics = await prepareWorkspace(page, state);
  const reviewViewports = [
    ['desktop-1280x800', 1280, 800],
    ['tablet-landscape-1024x768', 1024, 768],
    ['tablet-portrait-768x1024', 768, 1024],
    ['android-tablet-portrait-800x1280', 800, 1280]
  ] as const;
  await page.setViewportSize({ width: 1024, height: 768 });
  await openList(page);
  await openProfile(page);
  await page.getByRole('button', { name: 'الحضور' }).click();
  await expect(page.getByText('سجل الحضور والانصراف')).toBeVisible();
  await page.locator('#workerAttendanceFromDate').fill('2026-07-01');
  await page.locator('#workerAttendanceToDate').fill('2026-07-29');
  await page.getByRole('button', { name: 'تطبيق' }).click();
  await page.getByRole('button', { name: 'الصفحة التالية' }).click();
  await expect(page.getByText('attendance-record-2')).toBeVisible();

  for (const [name, width, height] of reviewViewports) {
    await page.setViewportSize({ width, height });
    const movementList = page.locator('.worker-profile__movement-list');
    const inChip = page.locator('.worker-profile__movement--in');
    const outChip = page.locator('.worker-profile__movement--out');
    await expect(movementList).toHaveCount(1);
    await expect(inChip).toHaveCount(1);
    await expect(outChip).toHaveCount(1);
    await expect(inChip).toContainText('حضور');
    await expect(outChip).toContainText('انصراف');
    const movementOrder = await movementList.locator('.worker-profile__movement').evaluateAll(elements => elements.map(element => element.textContent?.trim()));
    expect(movementOrder[0]).toContain('حضور');
    expect(movementOrder[1]).toContain('انصراف');
    expect(await inChip.evaluate(element => getComputedStyle(element).backgroundColor)).toBe('rgb(191, 255, 255)');
    expect(await outChip.evaluate(element => getComputedStyle(element).backgroundColor)).toBe('rgb(254, 255, 196)');
    const movementLayout = await movementList.evaluate(element => {
      const chips = Array.from(element.querySelectorAll<HTMLElement>('.worker-profile__movement'));
      const inIcon = chips[0]?.querySelector<HTMLElement>('.pi');
      const inLabel = chips[0]?.querySelector<HTMLElement>('b');
      const outIcon = chips[1]?.querySelector<HTMLElement>('.pi');
      const outLabel = chips[1]?.querySelector<HTMLElement>('b');
      const style = getComputedStyle(element);
      return {
        direction: style.direction,
        flexDirection: style.flexDirection,
        justifyContent: style.justifyContent,
        inLeft: chips[0]?.getBoundingClientRect().left ?? 0,
        outLeft: chips[1]?.getBoundingClientRect().left ?? 0,
        inRight: chips[0]?.getBoundingClientRect().right ?? 0,
        outRight: chips[1]?.getBoundingClientRect().right ?? 0,
        inTop: chips[0]?.getBoundingClientRect().top ?? 0,
        outTop: chips[1]?.getBoundingClientRect().top ?? 0,
        inIconLeft: inIcon?.getBoundingClientRect().left ?? 0,
        inLabelLeft: inLabel?.getBoundingClientRect().left ?? 0,
        outIconLeft: outIcon?.getBoundingClientRect().left ?? 0,
        outLabelLeft: outLabel?.getBoundingClientRect().left ?? 0,
        inIconClass: inIcon?.className ?? '',
        outIconClass: outIcon?.className ?? '',
        inIconTransform: inIcon ? getComputedStyle(inIcon).transform : '',
        outIconTransform: outIcon ? getComputedStyle(outIcon).transform : ''
      };
    });
    expect(movementLayout).toEqual(expect.objectContaining({
      direction: 'rtl',
      flexDirection: 'row',
      justifyContent: 'flex-start',
      inIconTransform: 'none',
      outIconTransform: 'none'
    }));
    if (Math.abs(movementLayout.inTop - movementLayout.outTop) < 1) {
      expect(movementLayout.inRight).toBeGreaterThan(movementLayout.outRight);
    } else {
      expect(movementLayout.inTop).toBeLessThan(movementLayout.outTop);
    }
    expect(movementLayout.inIconLeft).toBeGreaterThan(movementLayout.inLabelLeft);
    expect(movementLayout.outIconLeft).toBeGreaterThan(movementLayout.outLabelLeft);
    expect(movementLayout.inIconClass).toContain('pi-sign-in');
    expect(movementLayout.outIconClass).toContain('pi-sign-out');
    expect(await page.locator('.worker-profile__attendance-table').evaluate(element => getComputedStyle(element).direction)).toBe('rtl');
    const geometry = await page.evaluate(() => ({
      scrollWidth: document.documentElement.scrollWidth,
      clientWidth: document.documentElement.clientWidth
    }));
    expect(geometry.scrollWidth).toBeLessThanOrEqual(geometry.clientWidth + 1);
    await page.screenshot({ path: path.join(visualOutput, `${name}-attendance-rtl.png`), fullPage: true });
  }

  await page.getByRole('button', { name: 'التسكين والتشغيل' }).click();
  await expect(page.getByText('القسم التنظيمي داخل Dayoub')).toBeVisible();
  await page.getByRole('button', { name: 'تغيير القسم' }).click();
  await expect(page.getByText('تعيين تنظيمي داخل Dayoub فقط، ولا يغيّر ZKTime أو التسكين الإنتاجي.', { exact: true })).toBeVisible();
  for (const [name, width, height] of reviewViewports) {
    await page.setViewportSize({ width, height });
    const dialog = page.getByRole('dialog');
    const save = page.getByRole('button', { name: 'حفظ القسم' });
    await expect(dialog).toBeVisible();
    await expect(save).toBeVisible();
    const box = await dialog.boundingBox();
    expect(box?.x ?? -1).toBeGreaterThanOrEqual(0);
    expect(box?.y ?? -1).toBeGreaterThanOrEqual(0);
    expect((box?.x ?? 0) + (box?.width ?? 0)).toBeLessThanOrEqual(width + 1);
    expect((box?.y ?? 0) + (box?.height ?? 0)).toBeLessThanOrEqual(height + 1);
    await page.screenshot({ path: path.join(visualOutput, `${name}-department-dialog.png`), fullPage: true });
  }
  await page.locator('.worker-management-page__department-select').click();
  await page.getByRole('option', { name: /قسم التعبئة/ }).click();
  await page.getByRole('button', { name: 'حفظ القسم' }).click();
  await expect(page.getByText('قسم التعبئة', { exact: true })).toBeVisible();
  await expect(page.getByRole('button', { name: 'حفظ القسم' })).toHaveCount(0);
  expect(await page.locator('.p-component-overlay').count()).toBe(0);
  expect(diagnostics.consoleErrors).toEqual([]);
  expect(diagnostics.failedRequests).toEqual([]);
});
