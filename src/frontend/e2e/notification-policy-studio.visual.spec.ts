import { expect, Page, test } from '@playwright/test';
import { mkdir } from 'node:fs/promises';
import path from 'node:path';

const visualOutput = path.join(process.cwd(), 'test-results', 'notification-policy-studio');
const permissions = ['notifications.policies.manage'];
type Scenario = 'default' | 'empty' | 'error' | 'loading';

const workerPolicy = {
  eventKey: 'WorkerCreated', displayName: 'تم إنشاء عامل', allowedTokens: ['WorkerName', 'ActorName', 'FactoryName'],
  isEnabled: false, severity: 'Information', isToastEnabled: true, isInboxEnabled: true, isSoundEnabled: false,
  soundKey: null, titleTemplateAr: 'تم إنشاء عامل', messageTemplateAr: 'تم إنشاء العامل {WorkerName} بواسطة {ActorName} في {FactoryName}.',
  rowVersion: 'AQIDBAUGBwg=', recipientRules: [], updatedAtUtc: '2026-07-20T00:00:00.000Z'
};
const assignmentPolicy = {
  ...workerPolicy, eventKey: 'AssignmentChanged', displayName: 'تم تغيير التسكين', allowedTokens: ['WorkerName', 'ActorName', 'LineName', 'FactoryName'],
  severity: 'Warning', isEnabled: true, isSoundEnabled: true, soundKey: 'default', titleTemplateAr: 'تم تغيير التسكين',
  messageTemplateAr: 'تم تسكين العامل {WorkerName} في {LineName} داخل {FactoryName} بواسطة {ActorName}.', rowVersion: 'CQgHBgUEAwI='
};

test.beforeAll(async () => {
  await mkdir(visualOutput, { recursive: true });
});

async function prepareStudio(page: Page, scenario: Scenario = 'default'): Promise<{ updates: number; consoleErrors: string[]; failedRequests: string[] }> {
  const diagnostics = { updates: 0, consoleErrors: [] as string[], failedRequests: [] as string[] };
  let persistedWorkerPolicy = structuredClone(workerPolicy);
  page.on('console', message => { if (message.type() === 'error') diagnostics.consoleErrors.push(message.text()); });
  page.on('pageerror', error => diagnostics.consoleErrors.push(error.message));
  page.on('requestfailed', request => diagnostics.failedRequests.push(`${request.method()} ${request.url()}`));
  await page.addInitScript(({ storedPermissions }) => {
    localStorage.setItem('plp.accessToken', 'notification-policy-visual-token');
    localStorage.setItem('plp.currentUser', JSON.stringify({
      id: 'notification-policy-reviewer', fullName: 'مراجع سياسات الإشعارات', email: 'policy.review@local.test', roles: ['Administrator'], permissions: storedPermissions
    }));
  }, { storedPermissions: permissions });
  await page.routeWebSocket('**/hubs/notifications**', socket => {
    socket.onMessage(message => { if (typeof message === 'string' && message.includes('"protocol"')) socket.send('{}\u001e'); });
  });
  await page.route('**/hubs/notifications/negotiate**', route => route.fulfill({
    status: 200, contentType: 'application/json', body: JSON.stringify({ negotiateVersion: 1, connectionId: 'policy-visual', connectionToken: 'policy-visual', availableTransports: [{ transport: 'WebSockets', transferFormats: ['Text', 'Binary'] }] })
  }));
  await page.route('**/api/**', async route => {
    const pathname = new URL(route.request().url()).pathname;
    if (pathname.endsWith('/api/auth/me')) {
      await route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify({ success: true, data: { id: 'notification-policy-reviewer', fullName: 'مراجع سياسات الإشعارات', email: 'policy.review@local.test', roles: ['Administrator'], permissions }, error: null }) });
      return;
    }
    if (pathname === '/api/admin/notification-policies' && scenario === 'error') {
      await route.fulfill({ status: 500, contentType: 'application/json', body: JSON.stringify({ success: false, data: null, error: { code: 'Failure', message: 'تعذر تحميل السياسات' } }) });
      return;
    }
    if (pathname === '/api/admin/notification-policies' && scenario === 'loading') {
      await new Promise(resolve => setTimeout(resolve, 1600));
    }
    let data: unknown = { items: [] };
    if (pathname === '/api/admin/notification-policies') {
      data = scenario === 'empty' ? [] : [toListItem(workerPolicy), toListItem(assignmentPolicy)];
    } else if (pathname === '/api/admin/notification-policies/recipient-options') {
      data = { users: [{ id: 'f0000000-0000-0000-0000-000000000001', fullName: 'أحمد محمد', email: 'ahmed@test.local' }], roles: [{ id: 'f0000000-0000-0000-0000-000000000002', name: 'مشرف' }], permissions: [{ name: 'workers.view', capability: 'workers', descriptionAr: 'عرض بيانات العمال' }], capabilityGroups: ['workers'] };
    } else if (pathname.endsWith('/WorkerCreated')) {
      if (route.request().method() === 'PUT') {
        persistedWorkerPolicy = { ...persistedWorkerPolicy, ...route.request().postDataJSON(), rowVersion: 'ERITFBUWFxg=' };
        diagnostics.updates += 1;
      }
      data = persistedWorkerPolicy;
    } else if (pathname.endsWith('/AssignmentChanged')) {
      data = assignmentPolicy;
    }
    await route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify({ success: true, data, error: null }) });
  });
  return diagnostics;
}

function toListItem(policy: typeof workerPolicy): Record<string, unknown> {
  const { allowedTokens, soundKey, titleTemplateAr, messageTemplateAr, rowVersion, recipientRules, ...item } = policy;
  return item;
}

async function openStudio(page: Page): Promise<void> {
  await page.goto('/admin/notification-policies');
  await expect(page.getByRole('heading', { name: 'استوديو سياسات الإشعارات' })).toBeVisible();
  await expect(page.locator('.policy-studio__editor')).toBeVisible();
}

async function expectViewportSafe(page: Page, width: number): Promise<void> {
  const geometry = await page.evaluate(() => ({ direction: getComputedStyle(document.documentElement).direction, scrollWidth: document.documentElement.scrollWidth, clientWidth: document.documentElement.clientWidth }));
  expect(geometry.direction).toBe('rtl');
  expect(geometry.scrollWidth).toBeLessThanOrEqual(geometry.clientWidth + 1);
  const save = page.getByRole('button', { name: 'حفظ السياسة' });
  const box = await save.boundingBox();
  expect(box?.height ?? 0).toBeGreaterThanOrEqual(40);
  if (width <= 599) {
    await expect(page.locator('.policy-studio__catalog thead')).toBeHidden();
    await expect(page.locator('.policy-studio__catalog tbody tr')).toHaveCount(2);
  } else {
    await expect(page.locator('.policy-studio__catalog thead')).toBeVisible();
  }
}

test('renders an RTL, overflow-safe policy studio at the required viewports and saves a policy', async ({ page }) => {
  const diagnostics = await prepareStudio(page);
  const viewports = [
    ['desktop-1440x900', 1440, 900],
    ['android-tablet-landscape-1280x800', 1280, 800],
    ['android-tablet-portrait-800x1280', 800, 1280],
    ['mobile-390x844', 390, 844]
  ] as const;
  for (const [name, width, height] of viewports) {
    await page.setViewportSize({ width, height });
    await openStudio(page);
    await expectViewportSafe(page, width);
    await page.screenshot({ path: path.join(visualOutput, `${name}.png`), fullPage: true });
  }
  await page.locator('.policy-studio__templates').scrollIntoViewIfNeeded();
  await expect(page.getByText('معاينة مباشرة')).toBeVisible();
  await page.screenshot({ path: path.join(visualOutput, 'mobile-template-editor.png'), fullPage: true });
  await page.locator('.policy-studio__rules').scrollIntoViewIfNeeded();
  await expect(page.getByText('قواعد المستلمين')).toBeVisible();
  await page.screenshot({ path: path.join(visualOutput, 'mobile-recipient-rules.png'), fullPage: true });
  const workerToken = page.getByRole('button', { name: '{WorkerName}' });
  await expect(workerToken).toHaveCount(1);
  await workerToken.click();
  await page.getByRole('button', { name: 'إضافة قاعدة' }).click();
  await page.getByRole('button', { name: 'حفظ السياسة' }).click();
  await expect.poll(() => diagnostics.updates).toBe(1);
  expect(diagnostics.consoleErrors).toEqual([]);
  expect(diagnostics.failedRequests).toEqual([]);
  const menu = page.getByRole('button', { name: 'فتح القائمة' });
  await expect(menu).toHaveCount(1);
  await menu.click();
  await expect(page.locator('.plp-app-shell-overlay-nav')).toContainText('سياسات الإشعارات');
  await page.screenshot({ path: path.join(visualOutput, 'mobile-navigation.png'), fullPage: true });
});

test('shows explicit loading, empty, and error states', async ({ page }) => {
  await page.setViewportSize({ width: 390, height: 844 });
  await prepareStudio(page, 'loading');
  await page.goto('/admin/notification-policies', { waitUntil: 'domcontentloaded' });
  await expect(page.locator('.plp-product-loading')).toBeVisible();
  await page.screenshot({ path: path.join(visualOutput, 'mobile-loading.png'), fullPage: true });

  await prepareStudio(page, 'empty');
  await page.goto('/admin/notification-policies');
  await expect(page.getByText('لا توجد أحداث متاحة')).toBeVisible();
  await page.screenshot({ path: path.join(visualOutput, 'mobile-empty.png'), fullPage: true });

  await prepareStudio(page, 'error');
  await page.goto('/admin/notification-policies');
  await expect(page.getByTitle('تعذر إكمال العملية').locator('section[role="alert"]')).toContainText('تعذر تحميل السياسات');
  await expect(page.getByRole('button', { name: 'إعادة المحاولة' })).toBeVisible();
  await page.screenshot({ path: path.join(visualOutput, 'mobile-error.png'), fullPage: true });
});

test('keeps a role recipient selected after save and a policy reload', async ({ page }) => {
  await page.setViewportSize({ width: 1280, height: 800 });
  const diagnostics = await prepareStudio(page);
  await openStudio(page);

  await page.getByRole('button', { name: 'إضافة قاعدة' }).click();
  const rule = page.locator('.policy-studio__rule').last();
  await rule.locator('select').first().selectOption('Role');
  const roleSelect = rule.locator('select').nth(1);
  await roleSelect.selectOption('f0000000-0000-0000-0000-000000000002');
  await expect(roleSelect).toHaveValue('f0000000-0000-0000-0000-000000000002');

  await page.getByRole('button', { name: 'حفظ السياسة' }).click();
  await expect.poll(() => diagnostics.updates).toBe(1);
  await page.getByText('تم تغيير التسكين').first().click();
  await page.getByText('تم إنشاء عامل').first().click();

  await expect(page.locator('.policy-studio__rule').last().locator('select').nth(1))
    .toHaveValue('f0000000-0000-0000-0000-000000000002');
  expect(diagnostics.consoleErrors).toEqual([]);
  expect(diagnostics.failedRequests).toEqual([]);
});
