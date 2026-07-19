import { expect, Page, test } from '@playwright/test';
import { mkdir } from 'node:fs/promises';
import path from 'node:path';

const visualOutput = path.join(process.cwd(), 'test-results', 'worker-management');
const scenarioKey = 'plp.worker-management.mock-scenario';
const permissions = ['workers.view', 'workers.manage', 'assignments.view'];

type Scenario = 'default' | 'empty' | 'error' | 'loading';
type Diagnostics = { consoleErrors: string[]; failedRequests: string[]; unexpectedResponses: string[]; workerApiCalls: number };

test.beforeAll(async () => {
  await mkdir(visualOutput, { recursive: true });
});

async function prepareWorkspace(page: Page, scenario: Scenario = 'default'): Promise<Diagnostics> {
  const diagnostics: Diagnostics = { consoleErrors: [], failedRequests: [], unexpectedResponses: [], workerApiCalls: 0 };
  page.on('console', message => { if (message.type() === 'error') diagnostics.consoleErrors.push(message.text()); });
  page.on('pageerror', error => diagnostics.consoleErrors.push(error.message));
  page.on('requestfailed', request => diagnostics.failedRequests.push(`${request.method()} ${request.url()} ${request.failure()?.errorText ?? ''}`));
  page.on('response', response => { if (response.status() >= 400) diagnostics.unexpectedResponses.push(`${response.status()} ${response.url()}`); });

  await page.addInitScript(({ storedPermissions, selectedScenario, storageKey }) => {
    localStorage.setItem('plp.accessToken', 'worker-management-visual-token');
    localStorage.setItem('plp.currentUser', JSON.stringify({
      id: 'visual-worker-manager',
      fullName: 'مراجع إدارة العاملين',
      email: 'worker.management@local.test',
      roles: ['Administrator'],
      permissions: storedPermissions
    }));
    sessionStorage.setItem(storageKey, selectedScenario);
  }, { storedPermissions: permissions, selectedScenario: scenario, storageKey: scenarioKey });

  await page.route('**/api/**', async route => {
    const pathname = new URL(route.request().url()).pathname;
    if (pathname.startsWith('/api/workers')) diagnostics.workerApiCalls += 1;
    const data = pathname.endsWith('/api/auth/me')
      ? { id: 'visual-worker-manager', fullName: 'مراجع إدارة العاملين', email: 'worker.management@local.test', roles: ['Administrator'], permissions }
      : { items: [] };
    await route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify({ success: true, data, error: null }) });
  });
  return diagnostics;
}

async function openList(page: Page): Promise<void> {
  await page.goto('/workers');
  await expect(page.getByRole('heading', { name: 'إدارة العاملين' })).toBeVisible();
  await expect(page.locator('.worker-management-page__table')).toBeVisible();
}

async function expectViewportSafe(page: Page): Promise<void> {
  const geometry = await page.evaluate(() => ({
    direction: getComputedStyle(document.documentElement).direction,
    scrollWidth: document.documentElement.scrollWidth,
    clientWidth: document.documentElement.clientWidth
  }));
  expect(geometry.direction).toBe('rtl');
  expect(geometry.scrollWidth).toBeLessThanOrEqual(geometry.clientWidth + 1);
  await expect(page.locator('p-table[data-plp-table-presentation="stack"]')).toHaveCount(1);
  const openButtons = page.getByRole('button', { name: 'فتح الملف' });
  await expect(openButtons).toHaveCount(6);
  const openButton = openButtons.first();
  const box = await openButton.boundingBox();
  expect(box?.height ?? 0).toBeGreaterThanOrEqual(40);
}

test('renders an RTL, overflow-safe worker table at the required viewports', async ({ page }) => {
  const diagnostics = await prepareWorkspace(page);
  const viewports = [
    ['desktop-1440x900', 1440, 900],
    ['android-tablet-landscape-1280x800', 1280, 800],
    ['android-tablet-portrait-800x1280', 800, 1280],
    ['mobile-390x844', 390, 844]
  ] as const;

  for (const [name, width, height] of viewports) {
    await page.setViewportSize({ width, height });
    await openList(page);
    await expectViewportSafe(page);
    if (width <= 1023) {
      await expect(page.locator('.p-datatable-thead')).toBeHidden();
      await expect(page.locator('.p-datatable-tbody > tr').first()).toHaveCSS('display', 'grid');
    } else {
      await expect(page.locator('.p-datatable-thead')).toBeVisible();
      const tableScroll = await page.locator('.p-datatable-wrapper').evaluate(element => ({
        clientWidth: element.clientWidth,
        scrollWidth: element.scrollWidth
      }));
      if (tableScroll.scrollWidth > tableScroll.clientWidth + 1) {
        await expect(page.locator('.plp-scroll-hint')).toBeVisible();
        await expect(page.locator('.plp-scroll-hint')).toHaveText('اسحب لعرض المزيد');
      }
    }
    const identities = page.locator('.worker-management-page__identity');
    await expect(identities).toHaveCount(6);
    await expect(identities.first()).toContainText('الاسم المحلي الرئيسي');
    await expect(identities.first()).toContainText('من المصدر');
    await page.screenshot({ path: path.join(visualOutput, `${name}.png`), fullPage: true });
  }

  expect(diagnostics.workerApiCalls).toBe(0);
  expect(diagnostics.consoleErrors).toEqual([]);
  expect(diagnostics.failedRequests).toEqual([]);
  expect(diagnostics.unexpectedResponses).toEqual([]);
});

test('opens and closes the full profile workspace and keeps source fields read-only', async ({ page }) => {
  const diagnostics = await prepareWorkspace(page);
  await page.setViewportSize({ width: 1440, height: 900 });
  await openList(page);
  await page.getByLabel('بحث في ملفات العاملين').fill('هدى إبراهيم');
  await expect(page.locator('.worker-management-page__table')).toContainText('هدى إبراهيم سالم');
  const conflictOpenButtons = page.getByRole('button', { name: 'فتح الملف' });
  await expect(conflictOpenButtons).toHaveCount(1);
  await conflictOpenButtons.click();

  await expect(page.locator('[data-workspace-view="profile"]')).toBeVisible();
  await expect(page.getByRole('heading', { name: 'هدى إبراهيم سالم' })).toBeVisible();
  await expect(page.locator('[role="alert"]')).toContainText('تعارض يحتاج مراجعة هوية');
  await expect(page.getByRole('button', { name: 'حفظ فعلي لاحقًا' })).toBeDisabled();
  await expect(page.getByRole('button', { name: 'تغيير الصورة لاحقًا' })).toBeDisabled();

  await page.getByRole('button', { name: 'بيانات المصدر', exact: true }).click();
  const sourceFields = page.locator('[data-profile-section="source"] input');
  await expect(sourceFields).toHaveCount(7);
  for (let index = 0; index < 7; index += 1) await expect(sourceFields.nth(index)).toHaveAttribute('readonly', '');

  await page.getByRole('button', { name: 'التسكين والتشغيل' }).click();
  await expect(page.locator('[data-profile-section="operations"]')).toContainText('مصنع التجميع');
  await page.getByRole('button', { name: 'السجل' }).click();
  await expect(page.locator('[data-profile-section="history"]')).toContainText('رصد اختلاف في الهوية');
  await page.getByRole('button', { name: 'معاينة بيانات المصدر' }).click();
  const preview = page.locator('[data-profile-section="source-preview"]');
  await expect(preview).toContainText('لن يُطبق أي تغيير');
  await expect(preview.getByRole('button')).toHaveCount(0);
  await page.screenshot({ path: path.join(visualOutput, 'desktop-identity-conflict-profile.png'), fullPage: true });

  await page.getByRole('button', { name: 'العودة إلى قائمة العاملين' }).click();
  await expect(page.getByRole('heading', { name: 'إدارة العاملين' })).toBeVisible();
  expect(diagnostics.workerApiCalls).toBe(0);
  expect(diagnostics.consoleErrors).toEqual([]);
  expect(diagnostics.failedRequests).toEqual([]);
});

test('keeps long names, missing photos, many stages, and mobile sections viewport-safe', async ({ page }) => {
  const diagnostics = await prepareWorkspace(page);
  await page.setViewportSize({ width: 390, height: 844 });
  await openList(page);

  await page.getByLabel('بحث في ملفات العاملين').fill('عبد الرحمن');
  await expect(page.locator('.worker-management-page__table')).toContainText('عبد الرحمن محمد عبد السلام');
  const longNameOpenButtons = page.getByRole('button', { name: 'فتح الملف' });
  await expect(longNameOpenButtons).toHaveCount(1);
  await longNameOpenButtons.click();
  await expect(page.locator('.worker-profile__identity h1')).toContainText('عبد الرحمن محمد عبد السلام');
  await expect(page.locator('.worker-profile__identity img')).toHaveCount(0);
  await expect(page.getByText('لا توجد — يظهر البديل القياسي')).toBeVisible();
  await expectViewportSafeProfile(page);
  await page.screenshot({ path: path.join(visualOutput, 'mobile-long-name-missing-photo.png'), fullPage: true });

  await page.getByRole('button', { name: 'العودة إلى قائمة العاملين' }).click();
  await page.getByLabel('بحث في ملفات العاملين').fill('كريم فتحي');
  await expect(page.locator('.worker-management-page__table')).toContainText('كريم فتحي');
  const manyStagesOpenButtons = page.getByRole('button', { name: 'فتح الملف' });
  await expect(manyStagesOpenButtons).toHaveCount(1);
  await manyStagesOpenButtons.click();
  await page.getByRole('button', { name: 'التسكين والتشغيل' }).click();
  await expect(page.locator('.worker-profile__stage-list span')).toHaveCount(6);
  await expectViewportSafeProfile(page);
  await page.screenshot({ path: path.join(visualOutput, 'mobile-many-stages.png'), fullPage: true });

  expect(diagnostics.consoleErrors).toEqual([]);
  expect(diagnostics.failedRequests).toEqual([]);
});

test('asserts loading, empty, and error states rather than only capturing screenshots', async ({ page }) => {
  await page.setViewportSize({ width: 390, height: 844 });

  await prepareWorkspace(page, 'loading');
  await page.goto('/workers');
  const loading = page.locator('.plp-product-loading');
  await expect(loading).toBeVisible();
  await expect(loading).toHaveAttribute('aria-busy', 'true');
  const skeletons = loading.locator('.p-skeleton');
  await expect(skeletons).toHaveCount(24);
  await expect(skeletons.first()).toBeVisible();
  await page.screenshot({ path: path.join(visualOutput, 'mobile-loading.png'), fullPage: true });
});

test('shows an explicit empty state', async ({ page }) => {
  await page.setViewportSize({ width: 390, height: 844 });
  const diagnostics = await prepareWorkspace(page, 'empty');
  await page.goto('/workers');
  await expect(page.getByText('لا توجد نتائج')).toBeVisible();
  await expect(page.locator('.worker-management-page__table')).toHaveCount(0);
  await page.screenshot({ path: path.join(visualOutput, 'mobile-empty.png'), fullPage: true });
  expect(diagnostics.consoleErrors).toEqual([]);
});

test('shows an actionable API-like error state', async ({ page }) => {
  await page.setViewportSize({ width: 390, height: 844 });
  const diagnostics = await prepareWorkspace(page, 'error');
  await page.goto('/workers');
  const error = page.locator('section[role="alert"]');
  await expect(error).toBeVisible();
  await expect(error).toContainText('تعذر تحميل إدارة العاملين');
  await expect(page.getByRole('button', { name: 'إعادة المحاولة' })).toBeVisible();
  await page.screenshot({ path: path.join(visualOutput, 'mobile-error.png'), fullPage: true });
  expect(diagnostics.consoleErrors).toEqual([]);
});

async function expectViewportSafeProfile(page: Page): Promise<void> {
  const geometry = await page.evaluate(() => ({ scrollWidth: document.documentElement.scrollWidth, clientWidth: document.documentElement.clientWidth }));
  expect(geometry.scrollWidth).toBeLessThanOrEqual(geometry.clientWidth + 1);
  const sectionButtons = page.locator('.plp-section-navigation__item');
  await expect(sectionButtons).toHaveCount(5);
  const firstBox = await sectionButtons.first().boundingBox();
  expect(firstBox?.height ?? 0).toBeGreaterThanOrEqual(40);
}
