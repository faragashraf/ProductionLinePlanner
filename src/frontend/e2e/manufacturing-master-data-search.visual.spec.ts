import { expect, Page, test } from '@playwright/test';
import { mkdir } from 'node:fs/promises';
import path from 'node:path';

const visualOutput = path.join(process.cwd(), 'test-results', 'manufacturing-master-data-search');
const permissions = ['dashboard.view', 'factories.view', 'production-lines.view', 'stages.view', 'stages.manage', 'models.view', 'models.manage', 'workers.view'];
const factory = { id: 'factory-1', code: 'F-01', name: 'مصنع الاختبار', isActive: true };
const department = { id: 'department-1', factoryId: factory.id, code: 'CUT', nameAr: 'قسم القص', isActive: true };
const line = { id: 'line-1', factoryId: factory.id, departmentId: department.id, lineCode: 'L-01', name: 'خط الإنتاج', sequenceOrder: 1, isActive: true };
const stages = [
  { id: 'stage-1', mainStageId: 'main-1', productionLineId: line.id, factoryId: factory.id, departmentId: department.id, productionLineName: line.name, departmentNameAr: department.nameAr, code: 'CUT-01', name: 'مرحلة القص', capacity: 5, defaultOrder: 1, isActive: true },
  { id: 'stage-2', mainStageId: 'main-1', productionLineId: line.id, factoryId: factory.id, departmentId: department.id, productionLineName: line.name, departmentNameAr: department.nameAr, code: 'SEW-02', name: 'مرحلة الخياطة', capacity: 6, defaultOrder: 2, isActive: true },
  { id: 'stage-3', mainStageId: 'main-1', productionLineId: line.id, factoryId: factory.id, departmentId: department.id, productionLineName: line.name, departmentNameAr: department.nameAr, code: 'PACK-03', name: 'Packing', capacity: 4, defaultOrder: 3, isActive: false }
];
const models = [
  { id: 'model-1', code: 'SHIRT-01', name: 'موديل قميص', isActive: true },
  { id: 'model-2', code: 'JACKET-02', name: 'Jacket', isActive: true },
  { id: 'model-3', code: 'PANTS-03', name: 'موديل بنطال', isActive: true },
  ...Array.from({ length: 49 }, (_, index) => ({ id: `model-fill-${index + 1}`, code: `FILL-${String(index + 1).padStart(2, '0')}`, name: `موديل إضافي ${index + 1}`, isActive: true })),
  { id: 'model-late', code: 'ZZZ-054', name: 'موديل في صفحة لاحقة', isActive: true }
];

test.beforeAll(async () => { await mkdir(visualOutput, { recursive: true }); });

async function preparePage(page: Page): Promise<void> {
  await page.addInitScript(({ userPermissions }) => {
    localStorage.setItem('plp.accessToken', 'master-data-visual-token');
    localStorage.setItem('plp.currentUser', JSON.stringify({ id: 'visual-user', fullName: 'مراجع الواجهة', email: 'visual@local.test', roles: ['Administrator'], permissions: userPermissions }));
  }, { userPermissions: permissions });
  await page.routeWebSocket('**/hubs/notifications**', socket => socket.onMessage(message => { if (typeof message === 'string' && message.includes('"protocol"')) socket.send('{}\u001e'); }));
  await page.route('**/hubs/notifications/negotiate**', route => route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify({ connectionId: 'master-data-visual', connectionToken: 'master-data-visual', availableTransports: [{ transport: 'WebSockets', transferFormats: ['Text', 'Binary'] }] }) }));
  await page.route('**/api/**', async route => {
    const url = new URL(route.request().url());
    const pathname = url.pathname;
    let data: unknown = { items: [] };
    if (pathname.endsWith('/api/auth/me')) data = { id: 'visual-user', fullName: 'مراجع الواجهة', email: 'visual@local.test', roles: ['Administrator'], permissions };
    else if (pathname.endsWith('/api/factories')) data = { items: [factory] };
    else if (pathname.endsWith('/api/departments')) data = { items: [department] };
    else if (pathname.endsWith('/api/production-lines')) data = { items: [line] };
    else if (pathname.endsWith('/api/stages')) data = { items: stages, totalCount: stages.length, pageNumber: 1, pageSize: 200 };
    else if (pathname.endsWith('/api/product-models')) {
      const search = (url.searchParams.get('search') ?? '').trim().toLocaleLowerCase();
      const filtered = search ? models.filter(model => [model.code, model.name].some(value => value.toLocaleLowerCase().includes(search))) : models;
      const page = Number(url.searchParams.get('page') ?? '1');
      const pageSize = Number(url.searchParams.get('pageSize') ?? '50');
      data = { items: filtered.slice((page - 1) * pageSize, page * pageSize), totalCount: filtered.length, pageNumber: page, pageSize };
    }
    else if (pathname.endsWith('/api/workers')) data = { items: [{ id: 'worker-1', code: 'W-001', fullName: 'عامل اختبار', isActive: true, employmentStatus: 'Active' }], totalCount: 1, pageNumber: 1, pageSize: 6, totalPages: 1 };
    await route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify({ success: true, data, error: null }) });
  });
}

async function expectViewportSafe(page: Page): Promise<void> {
  const geometry = await page.evaluate(() => {
    const main = document.querySelector('.plp-app-shell__main') as HTMLElement | null;
    return {
      direction: getComputedStyle(document.documentElement).direction,
      scrollWidth: document.documentElement.scrollWidth,
      clientWidth: document.documentElement.clientWidth,
      mainScrollWidth: main?.scrollWidth ?? 0,
      mainClientWidth: main?.clientWidth ?? 0
    };
  });
  expect(geometry.direction).toBe('rtl');
  expect(geometry.scrollWidth).toBeLessThanOrEqual(geometry.clientWidth + 1);
  expect(geometry.mainScrollWidth).toBeLessThanOrEqual(geometry.mainClientWidth + 1);
}

test('stage search is responsive, immediate, clearable, and empty-state safe', async ({ page }) => {
  await preparePage(page);
  for (const [name, width, height] of [['desktop-1440x900', 1440, 900], ['tablet-landscape-1280x800', 1280, 800], ['tablet-portrait-800x1280', 800, 1280], ['mobile-390x844', 390, 844]] as const) {
    await page.setViewportSize({ width, height });
    await page.goto('/manufacturing/stages');
    await expect(page.getByRole('heading', { name: 'مراحل الإنتاج' })).toBeVisible();
    await page.getByLabel('المصنع').selectOption(factory.id);
    await page.getByLabel('القسم').selectOption(department.id);
    await page.getByLabel('خط الإنتاج').selectOption(line.id);
    const search = page.getByPlaceholder('بحث باسم أو كود المرحلة');
    await expect(search).toBeVisible();
    await search.fill('  خيا ');
    await expect(page.locator('tbody')).toContainText('مرحلة الخياطة');
    await expect(page.locator('tbody')).not.toContainText('مرحلة القص');
    const clear = page.getByRole('button', { name: 'مسح البحث' });
    await expect(clear).toBeVisible();
    await clear.click();
    await expect(page.locator('tbody')).toContainText('مرحلة القص');
    await search.fill('sew-02');
    await expect(page.locator('tbody')).toContainText('مرحلة الخياطة');
    await clear.click();
    await search.fill('غير موجود');
    await expect(page.getByText('لا توجد مراحل مطابقة للبحث.')).toBeVisible();
    await page.screenshot({ path: path.join(visualOutput, `stages-${name}.png`), fullPage: true });
    await expectViewportSafe(page);
  }
});

test('model search is server-paginated and searches only model fields beyond the initial page', async ({ page }) => {
  await preparePage(page);
  for (const [name, width, height] of [['desktop-1440x900', 1440, 900], ['tablet-landscape-1280x800', 1280, 800], ['tablet-portrait-800x1280', 800, 1280], ['mobile-390x844', 390, 844]] as const) {
    await page.setViewportSize({ width, height });
    await page.goto('/manufacturing/models');
    await expect(page.getByRole('heading', { name: 'الموديلات وإعدادات المراحل' })).toBeVisible();
    const search = page.getByPlaceholder('بحث باسم أو كود الموديل');
    await expect(search).toBeVisible();
    await expect(page.locator('tbody')).not.toContainText('موديل في صفحة لاحقة');
    await search.fill('صفحة لاحقة');
    await expect(page.locator('tbody')).toContainText('موديل في صفحة لاحقة');
    await expect(page.locator('.p-paginator-current')).toContainText('من 1');
    await search.fill('  موديل قميص ');
    await expect(page.locator('tbody')).toContainText('موديل قميص');
    await expect(page.locator('tbody')).not.toContainText('Jacket');
    await search.fill('jacket-02');
    await expect(page.locator('tbody')).toContainText('Jacket');
    await expect(page.locator('tbody')).not.toContainText('موديل قميص');
    await search.fill('sew-02');
    await expect(page.getByText('لا توجد موديلات مطابقة للبحث.')).toBeVisible();
    await search.fill('لا نتيجة');
    await expect(page.getByText('لا توجد موديلات مطابقة للبحث.')).toBeVisible();
    const clear = page.getByRole('button', { name: 'مسح البحث' });
    await clear.click();
    await expect(page.locator('tbody')).toContainText('موديل بنطال');
    await page.screenshot({ path: path.join(visualOutput, `models-${name}.png`), fullPage: true });
    await expectViewportSafe(page);
  }
});

test('worker toolbar remains mobile-safe after the shared toolbar update', async ({ page }) => {
  await preparePage(page);
  await page.setViewportSize({ width: 390, height: 844 });
  await page.goto('/workers');
  await expect(page.getByPlaceholder('الاسم المحلي أو EmployeeCode')).toBeVisible();
  await expect(page.getByRole('button', { name: 'إعادة ضبط الفلاتر' })).toBeVisible();
  await expectViewportSafe(page);
  await page.screenshot({ path: path.join(visualOutput, 'workers-mobile-390x844.png'), fullPage: true });
});
