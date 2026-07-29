import { expect, Page, test } from '@playwright/test';
import { mkdir } from 'node:fs/promises';
import path from 'node:path';

const visualOutput = path.join(process.cwd(), 'test-results', 'notifications-page');
const permissions = ['attendance.view', 'assignments.view'];
const workerId = '11111111-1111-4111-8111-111111111111';
const notification = {
  id: '22222222-2222-4222-8222-222222222222', title: 'حضور عامل', message: 'سجل العامل حضورًا.', status: 'Unread', isRead: false,
  relatedEntityType: 'AttendanceRecord', relatedEntityId: null, eventKey: 'WorkerCheckedIn', severity: 'Information',
  createdAtUtc: '2026-07-29T05:01:00Z', readAtUtc: null, navigationUrl: '/attendance/workforce',
  metadataJson: JSON.stringify({ navigationAction: 'OpenDailyAttendance', navigationPayload: { workerId, productionDate: '2026-07-29' }, workerId, workerName: 'فاطمة عربي', employeeCode: '1001', attendanceType: 'CheckIn', attendanceTimeUtc: '2026-07-29T05:01:00Z', assignmentStatus: 'Assigned', stageName: 'التجميع', productionLineName: 'خط 1' })
};

test.beforeAll(async () => { await mkdir(visualOutput, { recursive: true }); });

async function prepare(page: Page, diagnostics: { reads: number; readAll: number; workforceQueries: string[]; errors: string[] }): Promise<void> {
  page.on('console', message => { if (message.type() === 'error') diagnostics.errors.push(message.text()); });
  page.on('pageerror', error => diagnostics.errors.push(error.message));
  await page.addInitScript(({ storedPermissions }) => {
    localStorage.setItem('plp.accessToken', 'notifications-visual-token');
    localStorage.setItem('plp.currentUser', JSON.stringify({ id: 'visual-user', fullName: 'مراجع الإشعارات', email: 'notifications@local.test', roles: ['Administrator'], permissions: storedPermissions }));
  }, { storedPermissions: permissions });
  await page.routeWebSocket('**/hubs/notifications**', socket => socket.onMessage(message => { if (typeof message === 'string' && message.includes('"protocol"')) socket.send('{}\u001e'); }));
  await page.route('**/hubs/notifications/negotiate**', route => route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify({ negotiateVersion: 1, connectionId: 'notifications-visual', connectionToken: 'notifications-visual', availableTransports: [{ transport: 'WebSockets', transferFormats: ['Text', 'Binary'] }] }) }));
  await page.route('**/api/**', async route => {
    const url = new URL(route.request().url());
    const pathname = url.pathname;
    let data: unknown = { items: [] };
    if (pathname.endsWith('/api/auth/me')) data = { id: 'visual-user', fullName: 'مراجع الإشعارات', email: 'notifications@local.test', roles: ['Administrator'], permissions };
    else if (pathname.endsWith('/api/notifications/unread-count')) data = { unreadCount: 1 };
    else if (pathname.endsWith('/api/notifications/read-all')) { diagnostics.readAll += 1; data = { updatedCount: 1 }; }
    else if (pathname.endsWith(`/api/notifications/${notification.id}/read`)) { diagnostics.reads += 1; data = { id: notification.id, isRead: true, readAtUtc: '2026-07-29T06:00:00Z' }; }
    else if (pathname.endsWith('/api/notifications')) data = { items: [notification], totalCount: 1, pageNumber: 1, pageSize: 20 };
    else if (pathname.endsWith('/api/attendance/workforce')) {
      diagnostics.workforceQueries.push(url.search);
      data = { productionDate: '2026-07-29', items: [], summary: { totalWorkers: 0, presentWorkers: 0, absentWorkers: 0, lateWorkers: 0, incompleteWorkers: 0, unassignedPresentWorkers: 0, assignedAbsentWorkers: 0, reviewRequiredWorkers: 0, attendanceDataAvailable: true, scope: 'filtered-results' }, page: 1, pageSize: 25, totalCount: 0, totalPages: 0 };
    }
    else if (pathname.endsWith('/api/factories')) data = { items: [] };
    await route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify({ success: true, data, error: null }) });
  });
}

async function expectSafeViewport(page: Page): Promise<void> {
  const geometry = await page.evaluate(() => ({ direction: getComputedStyle(document.documentElement).direction, scrollWidth: document.documentElement.scrollWidth, clientWidth: document.documentElement.clientWidth }));
  expect(geometry.direction).toBe('rtl');
  expect(geometry.scrollWidth).toBeLessThanOrEqual(geometry.clientWidth + 1);
  const readAll = page.getByRole('button', { name: 'تحديد الكل كمقروء' });
  await expect(readAll).toBeVisible();
  const box = await readAll.boundingBox();
  expect(box?.height ?? 0).toBeGreaterThanOrEqual(40);
  await expect(page.getByRole('button', { name: 'تحديد كمقروء' })).toBeVisible();
}

test('renders read controls safely in RTL on desktop, Android tablet, and phones', async ({ page }) => {
  const diagnostics = { reads: 0, readAll: 0, workforceQueries: [] as string[], errors: [] as string[] };
  await prepare(page, diagnostics);
  for (const [name, width, height] of [['desktop-1440x900', 1440, 900], ['android-tablet-800x1280', 800, 1280], ['mobile-390x844', 390, 844], ['mobile-360x800', 360, 800]] as const) {
    await page.setViewportSize({ width, height });
    await page.goto('/notifications');
    await expect(page.getByRole('heading', { name: 'الإشعارات' })).toBeVisible();
    await expectSafeViewport(page);
    await page.screenshot({ path: path.join(visualOutput, `${name}.png`), fullPage: true });
  }
  expect(diagnostics.errors).toEqual([]);
});

test('marks one notification read without navigation, then opens the action deep link', async ({ page }) => {
  const diagnostics = { reads: 0, readAll: 0, workforceQueries: [] as string[], errors: [] as string[] };
  await prepare(page, diagnostics);
  await page.setViewportSize({ width: 390, height: 844 });
  await page.goto('/notifications');
  await page.getByRole('button', { name: 'تحديد كمقروء' }).click();
  expect(diagnostics.reads).toBe(1);
  await expect(page).toHaveURL(/\/notifications$/);
  await page.locator('.plp-notification-card__content').click();
  await expect(page).toHaveURL(new RegExp(`/attendance/workforce\\?workerId=${workerId}&productionDate=2026-07-29`));
  await expect.poll(() => diagnostics.workforceQueries.length).toBe(1);
  expect(diagnostics.workforceQueries[0]).toContain(`workerId=${workerId}`);
  expect(diagnostics.workforceQueries[0]).toContain('productionDate=2026-07-29');
  await page.getByRole('button', { name: /إظهار الفلاتر/ }).click();
  await expect(page.getByRole('button', { name: 'إزالة فلتر العامل' })).toBeVisible();
  await page.screenshot({ path: path.join(visualOutput, 'mobile-attendance-deep-link-390x844.png'), fullPage: true });
  expect(diagnostics.errors).toEqual([]);
});

test('marks all notifications read across the inbox', async ({ page }) => {
  const diagnostics = { reads: 0, readAll: 0, workforceQueries: [] as string[], errors: [] as string[] };
  await prepare(page, diagnostics);
  await page.goto('/notifications');
  await page.getByRole('button', { name: 'تحديد الكل كمقروء' }).click();
  await expect.poll(() => diagnostics.readAll).toBe(1);
  await expect(page.getByText('تم تحديد 1 إشعار كمقروء.')).toBeVisible();
  expect(diagnostics.errors).toEqual([]);
});
