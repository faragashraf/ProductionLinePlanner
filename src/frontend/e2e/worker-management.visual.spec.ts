import { expect, Page, test } from '@playwright/test';
import { mkdir } from 'node:fs/promises';
import path from 'node:path';

const visualOutput = path.join(process.cwd(), 'test-results', 'worker-management');
const workerId = '11111111-1111-1111-1111-111111111111';
const managerPermissions = ['workers.view', 'workers.manage', 'assignments.view'];
type Scenario = 'default' | 'loading' | 'empty' | 'error';

const worker = (hasPhoto = false) => ({
  id: workerId,
  employeeCode: 'EMP-101',
  fullName: 'فاطمة أحمد عبد الرحمن',
  attendanceUserId: '101',
  badgeNumber: 'B-101',
  isActive: true,
  employmentStatus: 'Active',
  defaultSubStageId: null,
  hasPhoto,
  photoReference: hasPhoto ? `/api/workers/${workerId}/photo?v=${'a'.repeat(64)}` : null,
  photoVersion: hasPhoto ? 'a'.repeat(64) : null
});

test.beforeAll(async () => { await mkdir(visualOutput, { recursive: true }); });

async function prepareWorkspace(page: Page, scenario: Scenario = 'default', permissions = managerPermissions): Promise<void> {
  let hasPhoto = false;
  await page.addInitScript(({ storedPermissions }) => {
    localStorage.setItem('plp.accessToken', 'worker-management-visual-token');
    localStorage.setItem('plp.currentUser', JSON.stringify({
      id: 'visual-worker-manager', fullName: 'مراجع إدارة العاملين', email: 'worker.management@local.test',
      roles: ['Administrator'], permissions: storedPermissions
    }));
  }, { storedPermissions: permissions });

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
    if (pathname.endsWith('/api/auth/me')) {
      await route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify({ success: true, data: { id: 'visual-worker-manager', fullName: 'مراجع إدارة العاملين', email: 'worker.management@local.test', roles: ['Administrator'], permissions }, error: null }) });
      return;
    }
    if (pathname === '/api/workers' && request.method() === 'GET') {
      if (scenario === 'loading') { await new Promise(resolve => setTimeout(resolve, 1000)); }
      if (scenario === 'error') { await route.fulfill({ status: 500, contentType: 'application/json', body: JSON.stringify({ success: false, data: null, error: { message: 'تعذر تحميل العاملين.' } }) }); return; }
      const items = scenario === 'empty' ? [] : [worker(hasPhoto)];
      await route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify({ success: true, data: { items, totalCount: items.length, pageNumber: 1, pageSize: 6 }, error: null }) });
      return;
    }
    if (pathname === `/api/workers/${workerId}` && request.method() === 'GET') {
      await route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify({ success: true, data: worker(hasPhoto), error: null }) });
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
    if (pathname === `/api/workers/${workerId}/photo` && request.method() === 'PUT') {
      hasPhoto = true;
      await route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify({ success: true, data: { photo: { version: 'a'.repeat(64) } }, error: null }) });
      return;
    }
    if (pathname === `/api/workers/${workerId}/photo` && request.method() === 'DELETE') {
      hasPhoto = false;
      await route.fulfill({ status: 204 });
      return;
    }
    if (pathname === `/api/workers/${workerId}/photo`) {
      await route.fulfill({ status: 404, contentType: 'application/json', body: JSON.stringify({ success: false, error: { message: 'غير متاح' } }) });
      return;
    }
    await route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify({ success: true, data: { items: [] }, error: null }) });
  });
}

async function openList(page: Page): Promise<void> {
  await page.goto('/workers');
  await expect(page.getByRole('heading', { name: 'إدارة العاملين' })).toBeVisible();
  await expect(page.locator('.worker-management-page__table')).toBeVisible();
}

async function expectViewportSafe(page: Page): Promise<void> {
  const geometry = await page.evaluate(() => ({ direction: getComputedStyle(document.documentElement).direction, scrollWidth: document.documentElement.scrollWidth, clientWidth: document.documentElement.clientWidth }));
  expect(geometry.direction).toBe('rtl');
  expect(geometry.scrollWidth).toBeLessThanOrEqual(geometry.clientWidth + 1);
  const openButton = page.getByRole('button', { name: 'فتح الملف' });
  await expect(openButton).toHaveCount(1);
  expect((await openButton.boundingBox())?.height ?? 0).toBeGreaterThanOrEqual(40);
}

test('uses API-backed worker data and remains RTL/overflow safe at required viewports', async ({ page }) => {
  await prepareWorkspace(page);
  for (const [name, width, height] of [
    ['desktop-1440x900', 1440, 900], ['android-tablet-landscape-1280x800', 1280, 800],
    ['android-tablet-portrait-800x1280', 800, 1280], ['mobile-390x844', 390, 844]
  ] as const) {
    await page.setViewportSize({ width, height });
    await openList(page);
    await expect(page.locator('.worker-management-page__table')).toContainText('فاطمة أحمد عبد الرحمن');
    await expectViewportSafe(page);
    await page.screenshot({ path: path.join(visualOutput, `${name}.png`), fullPage: true });
  }
});

test('renders the real profile, validates photo selection, replaces and deletes with confirmation', async ({ page }) => {
  await prepareWorkspace(page);
  await page.setViewportSize({ width: 390, height: 844 });
  await openList(page);
  await page.getByRole('button', { name: 'فتح الملف' }).click();
  await expect(page.locator('[data-workspace-view="profile"]')).toBeVisible();
  await expect(page.getByText('لا تقرأ هذه الشاشة نظام البصمة مباشرةً')).toBeVisible();
  await expect(page.getByText('JPEG وPNG وBMP فقط')).toBeVisible();
  await page.locator('#workerPhotoInput').setInputFiles({ name: 'worker.png', mimeType: 'image/png', buffer: Buffer.from('image-data') });
  await expect(page.getByRole('button', { name: 'رفع الصورة' })).toBeEnabled();
  await page.getByRole('button', { name: 'رفع الصورة' }).click();
  await expect(page.getByText('تم حفظ الصورة المحلية وتحديثها فورًا.')).toBeVisible();
  page.once('dialog', dialog => dialog.accept());
  await page.getByRole('button', { name: 'حذف الصورة' }).click();
  await expect(page.getByText('تم حذف الصورة المحلية.')).toBeVisible();
  const geometry = await page.evaluate(() => ({ scrollWidth: document.documentElement.scrollWidth, clientWidth: document.documentElement.clientWidth }));
  expect(geometry.scrollWidth).toBeLessThanOrEqual(geometry.clientWidth + 1);
  await page.screenshot({ path: path.join(visualOutput, 'mobile-photo-lifecycle.png'), fullPage: true });
});

test('shows loading, empty, and error states with API responses, not runtime mocks', async ({ page }) => {
  await page.setViewportSize({ width: 390, height: 844 });
  await prepareWorkspace(page, 'loading');
  await page.goto('/workers', { waitUntil: 'commit' });
  await expect(page.locator('.plp-product-loading')).toBeVisible();

  await prepareWorkspace(page, 'empty');
  await page.goto('/workers');
  await expect(page.getByText('لا توجد نتائج')).toBeVisible();

  await prepareWorkspace(page, 'error');
  await page.goto('/workers');
  await expect(page.getByRole('button', { name: 'إعادة المحاولة' })).toBeVisible();
});

test('hides write actions for workers.view-only users', async ({ page }) => {
  await prepareWorkspace(page, 'default', ['workers.view']);
  await openList(page);
  await page.getByRole('button', { name: 'فتح الملف' }).click();
  await expect(page.getByText('تتطلب التعديلات والصور `workers.manage`.')).toBeVisible();
  await expect(page.getByRole('button', { name: 'حفظ البيانات المحلية' })).toHaveCount(0);
  await expect(page.getByRole('button', { name: 'رفع الصورة' })).toBeDisabled();
});
