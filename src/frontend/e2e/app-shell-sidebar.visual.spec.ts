import { expect, Page, test } from '@playwright/test';
import { mkdir } from 'node:fs/promises';
import path from 'node:path';

const visualOutput = path.join(process.cwd(), 'test-results', 'app-shell-sidebar');
const productName = 'DAYOUB';
const arabicProductName = 'منصة ديوب';

test.beforeAll(async () => {
  await mkdir(visualOutput, { recursive: true });
});

async function openShell(page: Page): Promise<void> {
  await page.addInitScript(() => {
    localStorage.setItem('plp.accessToken', 'sidebar-visual-qa-token');
    localStorage.setItem('plp.currentUser', JSON.stringify({
      id: 'sidebar-visual-user',
      fullName: 'مراجع الواجهة',
      email: 'sidebar.visual@local.test',
      roles: ['Administrator'],
      permissions: ['dashboard.view']
    }));
  });

  await page.route('**/api/**', async route => {
    const pathname = new URL(route.request().url()).pathname;
    const data = pathname.endsWith('/api/auth/me')
      ? {
          id: 'sidebar-visual-user',
          fullName: 'مراجع الواجهة',
          email: 'sidebar.visual@local.test',
          roles: ['Administrator'],
          permissions: ['dashboard.view']
        }
      : { items: [] };

    await route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify({ success: true, data, error: null })
    });
  });

  await page.goto('/dashboard');
  await expect(page.locator('.plp-app-shell')).toBeVisible();
}

async function expectSingleIdentity(page: Page, identitySelector: string): Promise<void> {
  const identity = page.locator(identitySelector);
  await expect(identity).toHaveCount(1);
  await expect(identity).toContainText(productName);
  await expect(identity).not.toContainText(arabicProductName);
  await expect(identity.locator('[data-plp-brand-variant="mark"]')).toHaveCount(1);
  await expect(identity.locator('.plp-brand-logo__wordmark')).toHaveCount(0);
  await expect(identity.locator('.plp-app-shell__sidebar-name')).toHaveText(productName);

  const geometry = await identity.evaluate(element => {
    const mark = element.querySelector('plp-brand-logo')?.getBoundingClientRect();
    const name = element.querySelector('.plp-app-shell__sidebar-name')?.getBoundingClientRect();
    const identityBox = element.getBoundingClientRect();
    if (!mark || !name) throw new Error('Sidebar identity geometry is incomplete.');

    return {
      markRight: mark.right,
      markLeft: mark.left,
      nameRight: name.right,
      nameLeft: name.left,
      identityLeft: identityBox.left,
      identityRight: identityBox.right,
      scrollWidth: element.scrollWidth,
      clientWidth: element.clientWidth
    };
  });

  expect(Math.min(geometry.markRight, geometry.nameRight))
    .toBeLessThanOrEqual(Math.max(geometry.markLeft, geometry.nameLeft));
  expect(geometry.markLeft).toBeGreaterThanOrEqual(geometry.identityLeft - 1);
  expect(geometry.markRight).toBeLessThanOrEqual(geometry.identityRight + 1);
  expect(geometry.nameLeft).toBeGreaterThanOrEqual(geometry.identityLeft - 1);
  expect(geometry.nameRight).toBeLessThanOrEqual(geometry.identityRight + 1);
  expect(geometry.scrollWidth).toBeLessThanOrEqual(geometry.clientWidth + 1);
}

test('desktop sidebar identity', async ({ page }) => {
  await page.setViewportSize({ width: 1440, height: 900 });
  await openShell(page);
  await expectSingleIdentity(page, '.plp-app-shell__desktop-nav .plp-app-shell__sidebar-identity');
  await page.screenshot({ path: path.join(visualOutput, 'desktop-sidebar-1440x900.png'), fullPage: true });
});

test('tablet landscape sidebar identity', async ({ page }) => {
  await page.setViewportSize({ width: 1280, height: 800 });
  await openShell(page);
  await expectSingleIdentity(page, '.plp-app-shell__desktop-nav .plp-app-shell__sidebar-identity');
  await page.screenshot({ path: path.join(visualOutput, 'tablet-landscape-sidebar-1280x800.png'), fullPage: true });
});

test('overlay drawer identity', async ({ page }) => {
  await page.setViewportSize({ width: 600, height: 1000 });
  await openShell(page);
  await page.getByRole('button', { name: 'فتح القائمة' }).click();
  const drawer = page.locator('.plp-app-shell-overlay-nav');
  await expect(drawer).toBeVisible();
  await expectSingleIdentity(page, '.plp-app-shell-overlay-nav .plp-app-shell__sidebar-identity');
  await page.screenshot({ path: path.join(visualOutput, 'overlay-drawer-600x1000.png'), fullPage: true });
});
