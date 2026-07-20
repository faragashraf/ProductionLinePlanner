import { Browser, BrowserContext, expect, Page, test } from '@playwright/test';

const environment = {
  storageState: process.env['PLP_REALTIME_E2E_STORAGE_STATE'],
  stageFactoryId: process.env['PLP_REALTIME_E2E_STAGE_FACTORY_ID'],
  stageDepartmentId: process.env['PLP_REALTIME_E2E_STAGE_DEPARTMENT_ID'],
  stageProductionLineId: process.env['PLP_REALTIME_E2E_STAGE_PRODUCTION_LINE_ID'],
  stageName: process.env['PLP_REALTIME_E2E_STAGE_NAME'],
  workerName: process.env['PLP_REALTIME_E2E_WORKER_NAME']
};
const enabled = process.env['PLP_REALTIME_E2E'] === '1' && Object.values(environment).every(Boolean);

/**
 * This suite deliberately uses no API or WebSocket mocks. It is opt-in because
 * it mutates the configured development environment through two real browser
 * contexts. Both contexts may use the same user when no second test account is
 * provisioned; they remain independent SignalR connections.
 */
test.describe('manufacturing realtime multi-user', () => {
  test.skip(!enabled, 'Set PLP_REALTIME_E2E=1 plus storage state, stage context, stage name, and worker name before running this destructive development-environment suite.');

  test('two contexts synchronize model, stage, worker, and reconnect mutations without page reload', async ({ browser }) => {
    const [contextA, contextB] = await Promise.all([authenticatedContext(browser), authenticatedContext(browser)]);
    const [a, b] = await Promise.all([contextA.newPage(), contextB.newPage()]);
    const bHub = observeManufacturingHub(b);
    const suffix = `${Date.now()}`;
    const modelCode = `RT-${suffix}`;
    const modelName = `RT Model ${suffix}`;

    try {
      await Promise.all([a.goto('/manufacturing/models'), b.goto('/manufacturing/models')]);
      await expect(a.getByRole('heading', { name: 'الموديلات وإعدادات المراحل' })).toBeVisible();
      await expect(b.getByRole('heading', { name: 'الموديلات وإعدادات المراحل' })).toBeVisible();
      await bHub.waitForSubscription('models');

      await a.getByRole('button', { name: 'إضافة موديل' }).click();
      await a.getByLabel('الكود').fill(modelCode);
      await a.getByLabel('الاسم').fill(modelName);
      await a.getByRole('button', { name: 'إضافة موديل' }).last().click();
      await expect(b.getByText(modelName)).toBeVisible();

      await a.locator('tr', { hasText: modelName }).getByRole('button', { name: 'تعديل الموديل' }).click();
      const updatedModelName = `${modelName} updated`;
      await a.getByLabel('الاسم').fill(updatedModelName);
      await a.getByRole('button', { name: 'تحديث' }).click();
      await expect(b.getByText(updatedModelName)).toBeVisible();

      const model = await plannerApi<{ data: { items: { id: string }[] } }>(a, `/product-models?includeInactive=true&search=${encodeURIComponent(modelCode)}&page=1&pageSize=10`);
      const modelId = model.data.items[0]?.id;
      expect(modelId).toBeTruthy();
      await plannerApi(a, `/product-models/${modelId}/activation?isActive=false`, { method: 'PATCH', body: '{}' });
      await b.getByPlaceholder('بحث باسم أو كود الموديل').fill(modelCode);
      await expect(b.locator('tbody')).toContainText('معطل');

      await Promise.all([a.goto('/manufacturing/stages'), b.goto('/manufacturing/stages')]);
      await bHub.waitForSubscription('stages');
      await Promise.all([configureStageContext(a), configureStageContext(b)]);
      await expect(b.getByText(environment.stageName!)).toBeVisible();
      await a.locator('tr', { hasText: environment.stageName! }).getByRole('button', { name: 'تعديل المرحلة' }).click();
      const updatedStageName = `${environment.stageName} RT ${suffix}`;
      await a.getByLabel('اسم المرحلة').fill(updatedStageName);
      await a.getByRole('button', { name: 'تحديث المرحلة' }).click();
      await expect(b.getByText(updatedStageName)).toBeVisible();

      await Promise.all([a.goto('/manufacturing/employees'), b.goto('/manufacturing/employees')]);
      await bHub.waitForSubscription('employees');
      await Promise.all([findWorker(a, environment.workerName!), findWorker(b, environment.workerName!)]);
      await a.locator('tr', { hasText: environment.workerName! }).getByRole('button', { name: 'فتح الملف' }).click();
      const updatedWorkerName = `${environment.workerName} RT ${suffix}`;
      await a.getByLabel('الاسم العربي/المحلي المعروض').fill(updatedWorkerName);
      await a.getByRole('button', { name: 'حفظ البيانات المحلية' }).click();
      await b.getByPlaceholder('الاسم المحلي أو EmployeeCode').fill(updatedWorkerName);
      await expect(b.getByText(updatedWorkerName, { exact: true })).toBeVisible();

      await contextB.setOffline(true);
      const reconnectedWorkerName = `${updatedWorkerName} reconnect`;
      await a.getByLabel('الاسم العربي/المحلي المعروض').fill(reconnectedWorkerName);
      await a.getByRole('button', { name: 'حفظ البيانات المحلية' }).click();
      await contextB.setOffline(false);
      await bHub.waitForSubscription('employees', 2);
      await b.getByPlaceholder('الاسم المحلي أو EmployeeCode').fill(reconnectedWorkerName);
      await expect(b.getByText(reconnectedWorkerName, { exact: true })).toBeVisible();
    } finally {
      await Promise.all([contextA.close(), contextB.close()]);
    }
  });
});

async function authenticatedContext(browser: Browser): Promise<BrowserContext> {
  return browser.newContext({ storageState: environment.storageState! });
}

async function configureStageContext(page: Page): Promise<void> {
  await page.getByLabel('المصنع').selectOption(environment.stageFactoryId!);
  await page.getByLabel('القسم').selectOption(environment.stageDepartmentId!);
  await page.getByLabel('خط الإنتاج').selectOption(environment.stageProductionLineId!);
}

async function findWorker(page: Page, workerName: string): Promise<void> {
  await page.getByPlaceholder('الاسم المحلي أو EmployeeCode').fill(workerName);
  await expect(page.getByText(workerName, { exact: true })).toBeVisible();
}

function observeManufacturingHub(page: Page): { waitForSubscription(screen: string, count?: number): Promise<void> } {
  let connected = false;
  const joins = new Map<string, number>();
  page.on('websocket', socket => {
    if (!socket.url().includes('/hubs/notifications')) return;
    connected = true;
    socket.on('framesent', frame => {
      const payload = String(frame.payload);
      if (!payload.includes('JoinManufacturingScreen')) return;
      for (const screen of ['models', 'stages', 'employees']) {
        if (payload.includes(`"${screen}"`)) joins.set(screen, (joins.get(screen) ?? 0) + 1);
      }
    });
  });

  return {
    async waitForSubscription(screen: string, count = 1): Promise<void> {
      await expect.poll(() => connected, { timeout: 10_000 }).toBe(true);
      await expect.poll(() => joins.get(screen) ?? 0, { timeout: 10_000 }).toBeGreaterThanOrEqual(count);
    }
  };
}

async function plannerApi<T = unknown>(page: Page, path: string, init: RequestInit = {}): Promise<T> {
  return page.evaluate(async ({ path, init }) => {
    const token = localStorage.getItem('plp.accessToken');
    const response = await fetch(`/api${path}`, {
      ...init,
      headers: {
        Authorization: token ? `Bearer ${token}` : '',
        'Content-Type': 'application/json',
        ...(init.headers ?? {})
      }
    });
    if (!response.ok) throw new Error(`Planner API ${response.status} for ${path}`);
    return await response.json();
  }, { path, init });
}
