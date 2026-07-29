import { expect, Page, test } from '@playwright/test';
import { mkdir } from 'node:fs/promises';
import path from 'node:path';

const visualOutput = path.join(process.cwd(), 'test-results', 'model-stage-bulk-copy');
const permissions = ['models.view', 'models.manage', 'factories.view', 'production-lines.view', 'stages.view'];
const factory = { id: 'factory-1', code: 'F-01', name: 'مصنع الاختبار', isActive: true };
const department = { id: 'department-1', factoryId: factory.id, code: 'CUT', nameAr: 'قسم القص', sequenceOrder: 1, isActive: true };
const sourceLine = { id: 'line-source', factoryId: factory.id, departmentId: department.id, lineCode: 'CUT-1', name: 'خط المصدر', sequenceOrder: 1, isActive: true };
const targetLine = { id: 'line-target', factoryId: factory.id, departmentId: department.id, lineCode: 'CUT-2', name: 'خط الهدف', sequenceOrder: 2, isActive: true };
const sourceModel = { id: 'model-source', code: 'M-01', name: 'موديل المصدر', isActive: true };
const targetModel = { id: 'model-target', code: 'M-02', name: 'موديل الهدف', isActive: true };
const stage = { id: 'stage-1', mainStageId: 'main-1', mainStageName: 'التشغيل', factoryId: factory.id, departmentId: department.id, departmentNameAr: department.nameAr, code: 'STG004', name: 'استلام 1', capacity: 5, defaultOrder: 1, sequenceOrder: 1, isActive: true };
const relationship = { id: 'model-stage-1', productModelId: sourceModel.id, productionLineId: sourceLine.id, subStageId: stage.id, departmentId: department.id, subStageCode: stage.code, subStageName: stage.name, stageOrder: 1, piecePrice: 1.25, standardSeconds: 30, compensationMode: 'SharedPercentage', isRequired: true, isActive: true };

test.beforeAll(async () => mkdir(visualOutput, { recursive: true }));

async function preparePage(page: Page): Promise<void> {
  await page.addInitScript(({ userPermissions }) => {
    localStorage.setItem('plp.accessToken', 'bulk-copy-visual-token');
    localStorage.setItem('plp.currentUser', JSON.stringify({ id: 'visual-user', fullName: 'مراجع الواجهة', roles: ['Administrator'], permissions: userPermissions }));
  }, { userPermissions: permissions });
  await page.routeWebSocket('**/hubs/notifications**', socket => socket.onMessage(message => {
    if (typeof message === 'string' && message.includes('"protocol"')) socket.send('{}\u001e');
  }));
  await page.route('**/hubs/notifications/negotiate**', route => route.fulfill({
    status: 200,
    contentType: 'application/json',
    body: JSON.stringify({ connectionId: 'bulk-copy-visual', connectionToken: 'bulk-copy-visual', availableTransports: [{ transport: 'WebSockets', transferFormats: ['Text', 'Binary'] }] })
  }));
  await page.route('**/api/**', async route => {
    const url = new URL(route.request().url());
    const pathname = url.pathname;
    let data: unknown = { items: [] };
    if (pathname.endsWith('/api/auth/me')) data = { id: 'visual-user', fullName: 'مراجع الواجهة', roles: ['Administrator'], permissions };
    else if (pathname.endsWith('/api/factories')) data = { items: [factory] };
    else if (pathname.endsWith('/api/departments')) data = { items: [department] };
    else if (pathname.endsWith('/api/production-lines')) data = { items: [sourceLine, targetLine] };
    else if (pathname.endsWith(`/api/product-models/${sourceModel.id}/production-lines/${sourceLine.id}/stages`)) data = [relationship];
    else if (pathname.includes(`/api/product-models/${targetModel.id}/production-lines/`) && pathname.endsWith('/stages')) data = [];
    else if (pathname.endsWith(`/api/product-models/${sourceModel.id}/production-lines/${sourceLine.id}/stages/copy`)) {
      const request = route.request().postDataJSON() as { previewOnly: boolean };
      data = {
        sourceFactoryId: factory.id,
        sourceDepartmentId: department.id,
        sourceProductionLineId: sourceLine.id,
        sourceProductModelId: sourceModel.id,
        targetFactoryId: factory.id,
        targetDepartmentId: department.id,
        targetProductionLineId: targetLine.id,
        targetProductModelId: targetModel.id,
        isPreview: request.previewOnly,
        requestedCount: 1,
        addedCount: 1,
        skippedCount: 0,
        failedCount: 0,
        addedStageIds: request.previewOnly ? [] : ['copied-model-stage'],
        plannedStages: [{ sourceProductModelStageId: relationship.id, subStageId: stage.id, departmentId: department.id, productionLineId: targetLine.id, subStageCode: stage.code, subStageName: stage.name, stageOrder: 1, targetStageOrder: 1, createsTargetStage: false, statusLabel: 'المرحلة موجودة في القسم الهدف وسترتبط بالموديل.' }],
        skippedStages: [],
        failedStages: [],
        validationErrors: []
      };
    }
    else if (pathname.endsWith('/api/product-models')) data = { items: [sourceModel, targetModel], totalCount: 2, pageNumber: 1, pageSize: 10 };
    else if (pathname.endsWith('/api/stages')) data = { items: [stage], totalCount: 1, pageNumber: 1, pageSize: 200 };
    await route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify({ success: true, data, error: null }) });
  });
}

async function openBulkCopyDialog(page: Page): Promise<void> {
  await page.goto('/manufacturing/models');
  await expect(page.getByRole('heading', { name: 'الموديلات وإعدادات المراحل' })).toBeVisible();
  const tree = page.locator('.master-page__model-context');
  await tree.locator('.p-tree-toggler').first().click();
  const sourceModelNode = tree.locator('.p-treenode-content', { hasText: `${sourceModel.code} — ${sourceModel.name}` });
  await sourceModelNode.locator('.p-tree-toggler').click();
  const departmentNode = tree.locator('.p-treenode-content', { hasText: department.nameAr });
  await departmentNode.locator('.p-tree-toggler').click();
  await tree.locator('.p-treenode-content', { hasText: sourceLine.name }).click();
  const stageCheckbox = page.getByLabel(`تحديد المرحلة ${stage.name} للنسخ`);
  await expect(stageCheckbox).toBeVisible();
  await stageCheckbox.click({ noWaitAfter: true });
  const copyButton = page.getByRole('button', { name: 'نسخ مراحل الموديل المحددة' });
  await expect(copyButton).toBeEnabled();
  await copyButton.click();
  await expect(page.getByRole('dialog')).toBeVisible();
}

test('one-stage direct-copy dialog remains RTL and viewport-safe', async ({ page }) => {
  test.setTimeout(120_000);
  await preparePage(page);
  for (const [name, width, height] of [['desktop-1440x900', 1440, 900], ['tablet-landscape-1280x800', 1280, 800], ['tablet-portrait-800x1280', 800, 1280], ['mobile-390x844', 390, 844]] as const) {
    await page.setViewportSize({ width, height });
    await openBulkCopyDialog(page);
    const dialog = page.getByRole('dialog');
    await dialog.getByLabel('الموديل الهدف').selectOption(targetModel.id);
    await dialog.getByLabel('المصنع الهدف').selectOption(factory.id);
    await dialog.getByLabel('القسم الهدف').selectOption(department.id);
    await dialog.getByLabel('خط الإنتاج الهدف').selectOption(targetLine.id);
    await dialog.getByRole('button', { name: 'مراجعة النسخ' }).click();
    await expect(dialog.getByText('المرحلة موجودة في القسم الهدف وسترتبط بالموديل.')).toBeVisible();
    await page.waitForTimeout(250);
    await expect(dialog.getByText('المراحل التي ستُضاف')).toBeVisible();
    await expect(dialog.getByText(`${stage.code}`)).toBeVisible();
    await expect(dialog.getByRole('button', { name: 'إلغاء' })).toBeVisible();
    await expect(dialog.locator('.plp-bulk-operation__selected-stage select')).toHaveCount(0);
    const geometry = await page.evaluate(() => {
      const dialogElement = document.querySelector<HTMLElement>('[role="dialog"]')!;
      const footerElement = dialogElement.querySelector<HTMLElement>('.p-dialog-footer')!;
      const footerButtons = [...footerElement.querySelectorAll<HTMLElement>('button')];
      const box = dialogElement.getBoundingClientRect();
      const footerBox = footerElement.getBoundingClientRect();
      return {
        direction: getComputedStyle(document.querySelector<HTMLElement>('.plp-bulk-operation')!).direction,
        pageScrollWidth: document.documentElement.scrollWidth,
        pageClientWidth: document.documentElement.clientWidth,
        dialogLeft: box.left,
        dialogRight: box.right,
        dialogTop: box.top,
        dialogBottom: box.bottom,
        footerBottom: footerBox.bottom,
        actionBottom: Math.max(...footerButtons.map(button => button.getBoundingClientRect().bottom)),
        viewportHeight: innerHeight,
        viewportWidth: innerWidth
      };
    });
    expect(geometry.direction).toBe('rtl');
    expect(geometry.pageScrollWidth).toBeLessThanOrEqual(geometry.pageClientWidth + 1);
    expect(geometry.dialogLeft).toBeGreaterThanOrEqual(-1);
    expect(geometry.dialogRight).toBeLessThanOrEqual(geometry.viewportWidth + 1);
    expect(geometry.dialogTop).toBeGreaterThanOrEqual(-1);
    expect(geometry.dialogBottom).toBeLessThanOrEqual(geometry.viewportHeight + 1);
    expect(geometry.footerBottom).toBeLessThanOrEqual(geometry.viewportHeight + 1);
    expect(geometry.actionBottom).toBeLessThanOrEqual(geometry.viewportHeight + 1);
    await page.screenshot({ path: path.join(visualOutput, `direct-copy-${name}.png`), fullPage: true });
  }
});
