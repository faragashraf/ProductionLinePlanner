import { expect, Page, test } from '@playwright/test';
import { mkdir } from 'node:fs/promises';
import path from 'node:path';

const visualOutput = path.join(process.cwd(), 'test-results', 'daily-production-worker-rows');
const permissions = ['production.view', 'production.record', 'assignments.manage'];

interface VisualScenario {
  workerCount: 1 | 2 | 3;
  stageCount?: 1 | 3;
  longData?: boolean;
  longStageName?: boolean;
  reviewWarning?: boolean;
  missingCheckout?: boolean;
  dailyOverride?: boolean;
}

test.beforeAll(async () => {
  await mkdir(visualOutput, { recursive: true });
});

async function mockDailyProduction(page: Page, scenario: VisualScenario): Promise<void> {
  const stageName = scenario.longStageName
    ? 'مرحلة التجميع النهائي والتغليف والفحص التشغيلي متعددة الخطوات'
    : 'مرحلة التجميع النهائي';
  const stageWarnings = scenario.reviewWarning
    ? ['توزيع النسب لهذه المرحلة يحتاج مراجعة مدير قبل الاحتساب وإقرار تشغيل الوردية.']
    : [];
  const stageCount = scenario.stageCount ?? 1;
  const percentages = scenario.workerCount === 3 ? [33.3333, 33.3333, 33.3334] : Array(scenario.workerCount).fill(100 / scenario.workerCount);
  const quantities = scenario.workerCount === 3 ? [166.667, 166.667, 166.666] : Array(scenario.workerCount).fill(500 / scenario.workerCount);
  const workers = Array.from({ length: scenario.workerCount }, (_, index) => ({
    workerId: `worker-${index + 1}`,
    workerCode: `W-${String(index + 1).padStart(3, '0')}`,
    workerName: scenario.longData && index === 0
      ? 'عامل تشغيل بخبرة متعددة واسم طويل لاختبار القراءة دون قص المحتوى الأساسي'
      : `عامل التشغيل ${index + 1}`,
    isOnActiveService: true,
    effectiveAssignmentType: index === 1 ? 'Temporary' : 'Permanent',
    attendanceStatus: 'Present',
    hasSourceCheckIn: true,
    isPresent: true,
    requiresAuthorizedOverride: false,
    suggestedPercentage: percentages[index],
    contributionStartsAtUtc: '2026-07-17T04:33:00Z',
    contributionEndsAtUtc: '2026-07-17T16:07:00Z',
    workerMinutes: 694,
    isProductionReady: true,
    exclusionReason: null,
    isAssignedWorker: true,
    isDailyOverride: false
  }));
  const allocations = workers.map((worker, index) => ({
    workerId: worker.workerId,
    workerCode: worker.workerCode,
    workerName: worker.workerName,
    percentage: percentages[index],
    inputQuantity: quantities[index],
    equivalentQuantity: quantities[index],
    calculatedEarning: scenario.longData && index === 0 ? 1_234_567.8912 : 250 + index * 37.5
  }));
  const totalEntitlement = allocations.reduce((total, worker) => total + worker.calculatedEarning, 0);
  const previewStages = Array.from({ length: stageCount }, (_, index) => ({
    productModelStageId: `stage-${index + 1}`,
    stageCode: `ST-${String(index + 1).padStart(2, '0')}`,
    stageName: index === 0 ? stageName : index === 1 ? 'مرحلة الفحص والتعبئة' : 'مرحلة التجهيز القصيرة',
    stageQuantity: index === 1 ? 420.5 : 500,
    stageCost: totalEntitlement + index * 125.25,
    compensationMode: 'SharedPercentage',
    warnings: index === 0 ? stageWarnings : [],
    workers: allocations
  }));
  const operationStages = previewStages.map((stage, index) => ({
    productModelStageId: stage.productModelStageId,
    subStageId: `sub-stage-${index + 1}`,
    mainStageName: 'التجميع',
    stageCode: stage.stageCode,
    stageName: stage.stageName,
    stageOrder: index + 1,
    piecePrice: .5,
    compensationMode: 'SharedPercentage',
    staffingStatus: 'Staffed',
    attendanceStatus: 'Ready',
    hasAbsentWorkers: false,
    hasNoSourceCheckInWorkers: false,
    isFinancialReviewPending: false,
    isReady: true,
    workers
  }));
  const apiResponse = (data: unknown) => ({ success: true, data, error: null });

  await page.addInitScript(({ storedPermissions }) => {
    localStorage.setItem('plp.accessToken', 'visual-qa-token');
    localStorage.setItem('plp.currentUser', JSON.stringify({
      id: 'visual-user',
      fullName: 'مراجع التشغيل',
      email: 'visual.qa@local.test',
      roles: ['Administrator'],
      permissions: storedPermissions
    }));
  }, { storedPermissions: permissions });

  await page.route('**/api/**', async route => {
    const requestUrl = new URL(route.request().url());
    const pathname = requestUrl.pathname;
    let data: unknown;

    if (pathname.endsWith('/api/auth/me')) {
      data = { id: 'visual-user', fullName: 'مراجع التشغيل', email: 'visual.qa@local.test', roles: ['Administrator'], permissions };
    } else if (pathname.includes('/api/factories')) {
      data = { items: [{ id: 'factory-1', name: 'مصنع الاختبار المرئي', code: 'F-01', isActive: true }] };
    } else if (pathname.includes('/api/production-lines')) {
      data = { items: [{ id: 'line-1', factoryId: 'factory-1', name: 'خط التشغيل اليومي', lineCode: 'L-01', sequenceOrder: 1, isActive: true }] };
    } else if (pathname.includes('/api/product-models')) {
      data = { items: [{ id: 'model-1', code: 'M-01', name: 'موديل الاختبار المرئي', isActive: true }] };
    } else if (pathname.includes('/api/attendance/sync/production-date/')) {
      data = { syncDateUtc: '2026-07-17T00:00:00Z', sourceUsersCount: scenario.workerCount, sourceCheckInsCount: scenario.workerCount,
        matchedWorkersCount: scenario.workerCount, unmatchedSourceUsersCount: 0, workersWithoutAttendanceCount: 0,
        insertedRecords: scenario.workerCount, updatedRecords: 0, skippedRecords: 0 };
    } else if (pathname.endsWith('/api/production/daily-operations/preview')) {
      data = { productionDate: '2026-07-17', lineQuantity: 500, previewToken: 'visual-preview', totalWorkerEntitlements: totalEntitlement * stageCount,
        stages: previewStages,
        workerTotals: allocations.map(worker => ({ workerId: worker.workerId, workerCode: worker.workerCode,
          workerName: worker.workerName, totalEntitlement: worker.calculatedEarning * stageCount })), warnings: [] };
    } else if (pathname.endsWith('/api/production/daily-operations')) {
      data = { factoryId: 'factory-1', factoryName: 'مصنع الاختبار المرئي', productionLineId: 'line-1', productionLineName: 'خط التشغيل اليومي',
        productModelId: 'model-1', productModelCode: 'M-01', productModelName: 'موديل الاختبار المرئي', productionDate: '2026-07-17',
        staffingContextVersion: 'visual-fixture-v1', totalStages: stageCount, readyStages: stageCount, stagesWithAbsentWorkers: 0,
        stagesWithNoSourceCheckIn: 0, stagesWithoutStaffing: 0, stagesRequiringCostReview: 0, activeWorkers: workers,
        stages: operationStages };
    } else {
      data = { items: [] };
    }

    await route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify(apiResponse(data)) });
  });
}

async function openWorkerExpansion(page: Page, scenario: VisualScenario): Promise<void> {
  await mockDailyProduction(page, scenario);
  await page.goto('/manufacturing/daily-production-operations');

  await page.locator('.daily-production-operations__context-field--date input').fill('2026-07-17');
  await page.locator('.daily-production-operations__context-field--factory select').selectOption('factory-1');
  await page.locator('.daily-production-operations__context-field--line select').selectOption('line-1');
  await page.locator('.daily-production-operations__context-field--model select').selectOption('model-1');
  await page.getByRole('button', { name: 'مزامنة حضور التاريخ' }).click();
  await expect(page.getByRole('button', { name: 'تحميل تشغيل اليوم' })).toBeEnabled();
  await page.getByRole('button', { name: 'تحميل تشغيل اليوم' }).click();
  await expect(page.locator('[data-workspace-layout="stage-master-detail"]')).toBeVisible();
  await page.getByLabel('كمية تشغيل الخط (تطبق مرة واحدة على كل مرحلة)').fill('500');
  await page.getByRole('button', { name: 'حساب معاينة موحّدة' }).click();
  await expect(page.locator('.daily-production-operations__preview-stages')).toBeVisible();
  const stageExpander = page.locator('.daily-production-operations__preview-stages .plp-table-expander');
  await expect(stageExpander).toHaveCount(scenario.stageCount ?? 1);
  await stageExpander.first().click();
  await expect(page.locator('.plp-expansion-worker-row')).toHaveCount(scenario.workerCount);
}

async function captureScenario(page: Page, scenario: VisualScenario, fileName: string): Promise<void> {
  await openWorkerExpansion(page, scenario);
  const section = page.locator('.daily-production-operations__preview-stages');
  await expect(section).toBeVisible();
  await section.scrollIntoViewIfNeeded();
  await page.screenshot({ path: path.join(visualOutput, fileName) });
  await expect(page.locator('.plp-expansion-worker-grid, .plp-expansion-worker-item')).toHaveCount(0);
  await expect(page.locator('[class*="accent"]')).toHaveCount(0);
  await expect(page.locator('.plp-expansion-worker-row')).toHaveCount(scenario.workerCount);
  const overflow = await page.locator('.plp-expansion-surface--workers').evaluate(element => ({
    scrollWidth: element.scrollWidth,
    clientWidth: element.clientWidth
  }));
  expect(overflow.scrollWidth).toBeLessThanOrEqual(overflow.clientWidth + 1);
  const pageOverflow = await page.evaluate(() => ({
    scrollWidth: document.documentElement.scrollWidth,
    clientWidth: document.documentElement.clientWidth
  }));
  expect(pageOverflow.scrollWidth).toBeLessThanOrEqual(pageOverflow.clientWidth + 1);

  const stageSummaries = page.locator('.daily-production-operations__stage-summary-row');
  await expect(stageSummaries).toHaveCount(scenario.stageCount ?? 1);
  await expect(stageSummaries.first()).toBeVisible();
  const summaryLayouts = await stageSummaries.evaluateAll(elements => elements.map(element => ({
    display: getComputedStyle(element).display,
    columns: getComputedStyle(element).gridTemplateColumns,
    scrollWidth: element.scrollWidth,
    clientWidth: element.clientWidth
  })));
  expect(summaryLayouts.every(layout => layout.display === 'grid')).toBeTruthy();
  expect(summaryLayouts.every(layout => layout.scrollWidth <= layout.clientWidth + 1)).toBeTruthy();
  expect(new Set(summaryLayouts.map(layout => layout.columns)).size).toBe(1);

  await expect(page.locator('.plp-expansion-worker-row').first().locator('[data-worker-meta="minutes"]'))
    .toHaveText('11 ساعة 34 دقيقة');
  await expect(page.locator('.plp-expansion-worker-row').first().locator('.plp-expansion-worker-heading plp-status-badge'))
    .toBeVisible();
  if (scenario.workerCount === 3) {
    const displayedValues = await page.locator('.plp-expansion-worker-row .plp-expansion-key-values').allTextContents();
    expect(displayedValues.join(' ')).not.toMatch(/\.\d{3,}/);
  }

  const firstTime = page.locator('.plp-expansion-worker-row').first().locator('.plp-expansion-worker-time');
  const timeParts = firstTime.locator(':scope > *');
  await expect(timeParts).toHaveCount(3);
  await expect(timeParts.nth(0)).toHaveAttribute('data-time', 'check-in');
  await expect(timeParts.nth(1)).toHaveAttribute('data-time-arrow', '');
  await expect(timeParts.nth(2)).toHaveAttribute('data-time', 'check-out');
  const timeOrder = await firstTime.evaluate(element => {
    const parts = Array.from(element.children) as HTMLElement[];
    const labels = parts.map(part => part.textContent?.trim() ?? '');
    const boxes = parts.map(part => {
      const box = part.getBoundingClientRect();
      return { left: box.left, right: box.right };
    });
    return {
      direction: getComputedStyle(element).direction,
      unicodeBidi: getComputedStyle(element).unicodeBidi,
      text: element.textContent?.replace(/\s+/g, ' ').trim(),
      labels,
      boxes
    };
  });
  expect(timeOrder.direction).toBe('ltr');
  expect(timeOrder.unicodeBidi).toBe('isolate');
  expect(timeOrder.text).toBe('07:33→19:07');
  expect(timeOrder.boxes[0].right).toBeLessThan(timeOrder.boxes[1].left);
  expect(timeOrder.boxes[1].right).toBeLessThan(timeOrder.boxes[2].left);
}

async function captureWorkerPresenceScenario(page: Page, scenario: VisualScenario, fileName: string): Promise<void> {
  await openWorkerExpansion(page, scenario);
  if (scenario.missingCheckout) {
    await page.evaluate(() => {
      const angular = (window as typeof window & {
        ng?: {
          getComponent: (element: Element) => {
            workerAllocationRows: Array<Record<string, unknown>>;
          };
          applyChanges: (component: unknown) => void;
        };
      }).ng;
      const host = document.querySelector('app-daily-production-operations-page');
      if (!angular || !host) throw new Error('Angular presentation fixture is unavailable.');
      const component = angular.getComponent(host);
      component.workerAllocationRows = component.workerAllocationRows.map((worker, index) => index === 0
        ? {
            ...worker,
            contributionEndsAtUtc: null,
            participationType: 'إضافة يومية'
          }
        : worker);
      angular.applyChanges(component);
    });
  }
  const section = page.locator('.daily-production-operations__worker-totals');
  await section.scrollIntoViewIfNeeded();
  await expect(section.locator('.daily-production-operations__worker-presence-heading')).toHaveCount(1);

  const cells = section.locator('.daily-production-operations__worker-presence-cell');
  await expect(cells).toHaveCount(scenario.workerCount);
  await expect(cells.first()).toBeVisible();
  await expect(cells.first()).toHaveAttribute('data-label', 'الحضور والتسكين');
  const firstTime = cells.first().locator('.plp-contribution-time-range');
  const timeParts = firstTime.locator(':scope > *');
  await expect(timeParts).toHaveCount(3);
  await expect(timeParts.nth(0)).toHaveAttribute('data-time', 'check-in');
  await expect(timeParts.nth(0)).toHaveText('07:33');
  await expect(timeParts.nth(1)).toHaveAttribute('data-time-arrow', '');
  await expect(timeParts.nth(1)).toHaveText('→');
  await expect(timeParts.nth(2)).toHaveAttribute('data-time', 'check-out');
  await expect(timeParts.nth(2)).toHaveText(scenario.missingCheckout ? '—' : '19:07');
  expect((await firstTime.textContent())?.replace(/\s+/g, '')).toBe(
    scenario.missingCheckout ? '07:33→—' : '07:33→19:07'
  );
  expect(await firstTime.evaluate(element => getComputedStyle(element).direction)).toBe('ltr');
  expect(await firstTime.evaluate(element => getComputedStyle(element).unicodeBidi)).toBe('isolate');
  await expect(cells.first()).toContainText('11 ساعة 34 دقيقة');
  await expect(cells.first()).toContainText(scenario.dailyOverride ? 'إضافة يومية' : 'تسكين أساسي');
  if (scenario.workerCount > 1) await expect(cells.nth(1)).toContainText('تعيين مؤقت');

  const pageOverflow = await page.evaluate(() => ({
    scrollWidth: document.documentElement.scrollWidth,
    clientWidth: document.documentElement.clientWidth
  }));
  expect(pageOverflow.scrollWidth).toBeLessThanOrEqual(pageOverflow.clientWidth + 1);
  await page.screenshot({ path: path.join(visualOutput, fileName), fullPage: true });
}

test('one worker desktop', async ({ page }) => {
  await page.setViewportSize({ width: 1440, height: 1000 });
  await captureScenario(page, { workerCount: 1 }, 'worker-1-desktop.png');
});

test('two workers desktop', async ({ page }) => {
  await page.setViewportSize({ width: 1440, height: 1000 });
  await captureScenario(page, { workerCount: 2 }, 'worker-2-desktop.png');
});

test('three workers desktop', async ({ page }) => {
  await page.setViewportSize({ width: 1440, height: 1000 });
  await captureScenario(page, { workerCount: 3 }, 'worker-3-desktop.png');
});

test('tablet portrait', async ({ page }) => {
  await page.setViewportSize({ width: 800, height: 1280 });
  await captureScenario(page, { workerCount: 3 }, 'tablet-portrait-800x1280.png');
});

test('tablet landscape', async ({ page }) => {
  await page.setViewportSize({ width: 1280, height: 800 });
  await captureScenario(page, { workerCount: 3 }, 'tablet-landscape-1280x800.png');
});

test('laptop stage summary', async ({ page }) => {
  await page.setViewportSize({ width: 1100, height: 800 });
  await captureScenario(page, { workerCount: 2 }, 'laptop-1100x800.png');
});

test('long worker name and entitlement', async ({ page }) => {
  await page.setViewportSize({ width: 1280, height: 800 });
  await captureScenario(page, { workerCount: 3, longData: true }, 'long-name-entitlement.png');
});

test('long stage name and review warning', async ({ page }) => {
  await page.setViewportSize({ width: 1280, height: 800 });
  await captureScenario(page, { workerCount: 2, longStageName: true, reviewWarning: true }, 'long-stage-review-message.png');
  await expect(page.locator('.daily-production-operations__stage-summary-status small')).toContainText('مراجعة مدير');
});

test('multiple stage operational columns stay aligned', async ({ page }) => {
  await page.setViewportSize({ width: 1440, height: 1000 });
  await captureScenario(page, { workerCount: 2, stageCount: 3 }, 'stage-alignment-desktop.png');
});

test('worker presence desktop', async ({ page }) => {
  await page.setViewportSize({ width: 1440, height: 900 });
  await captureWorkerPresenceScenario(page, { workerCount: 3 }, 'worker-presence-desktop.png');
});

test('worker presence laptop', async ({ page }) => {
  await page.setViewportSize({ width: 1100, height: 800 });
  await captureWorkerPresenceScenario(page, { workerCount: 3 }, 'worker-presence-laptop-1100x800.png');
});

test('worker presence tablet landscape', async ({ page }) => {
  await page.setViewportSize({ width: 1280, height: 800 });
  await captureWorkerPresenceScenario(page, { workerCount: 3 }, 'worker-presence-tablet-landscape.png');
});

test('worker presence tablet portrait', async ({ page }) => {
  await page.setViewportSize({ width: 800, height: 1280 });
  await captureWorkerPresenceScenario(page, { workerCount: 3 }, 'worker-presence-tablet-portrait.png');
});

test('worker presence handles missing checkout and daily override', async ({ page }) => {
  await page.setViewportSize({ width: 1440, height: 900 });
  await captureWorkerPresenceScenario(
    page,
    { workerCount: 1, missingCheckout: true, dailyOverride: true },
    'worker-presence-missing-checkout-override.png'
  );
});
