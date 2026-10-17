import { test, expect } from '@playwright/test';

const summary = { total: 3, completed: 1, failed: 2, successRate: 1 / 3, errors: [{ code: 'timeout', count: 2 }],
  repeatedErrors: [{ code: 'timeout', count: 2 }], visibility: { reviewCandidateCount: 2, reviewCandidateLimit: 20,
    reviewCandidates: [], latency: { p95Ms: 200 }, statusDistribution: [], byCompany: [], byConnection: [] }, comparison: { available: false } };

test.beforeEach(async ({ page, baseURL }) => {
  await page.addInitScript(() => { window.__grafirioTestRole = 'admin'; });
  await page.route('**/*', async (route) => {
    const url = new URL(route.request().url());
    if (url.origin !== baseURL) return route.abort('blockedbyclient');
    const path = url.pathname;
    const json = (value) => route.fulfill({ json: value });
    if (path === '/ops/status') return json({ checkedAt: Date.now(), services: [{ id: 'api', name: 'Analiz API', status: 'unknown' }], docker: { available: true } });
    if (path === '/ops/audit') return json([]);
    if (path === '/data-analysis/api/admin/diagnostics/summary') return json(summary);
    if (path === '/data-analysis/api/admin/diagnostics') return json({ items: [], total: 0, summary });
    if (path === '/benchmark/api/scenario-runs') return json({ items: [] });
    if (path === '/v1/identity/companies') return json([{ id: 'fixture-company', name: 'Sentetik Firma', code: 'TEST', level: 0, isActive: true }]);
    if (path === '/v1/identity/users/company/fixture-company') return json([{ id: 'member', keycloakUserId: 'fixture-user', role: 'ADMIN' }]);
    if (path === '/v1/identity/subscriptions/company/fixture-company') return json([{ id: 'fixture-sub', plan: 'STANDARD', status: 'ACTIVE', isCurrentlyActive: true }]);
    if (path === '/v1/identity/subscriptions/fixture-sub' || path === '/v1/identity/users/fixture-user/companies/fixture-company/role') return json(true);
    if (path.startsWith('/ops/') || path.startsWith('/v1/') || path.startsWith('/benchmark/') || path.startsWith('/data-analysis/')) return route.fulfill({ status: 503, json: { error: 'Sentetik kaynak erişilemiyor' } });
    return route.continue();
  });
});

test('dashboard does not label unknown services healthy and links to the scoped query list', async ({ page }) => {
  await page.goto('/');
  await expect(page.getByRole('heading', { name: 'Genel Bakış' })).toBeVisible();
  await expect(page.getByText('Sistem durumu doğrulanamadı', { exact: true })).toBeVisible();
  await expect(page.getByRole('navigation', { name: 'Ana menü' }).getByRole('link')).toHaveCount(6);
  await page.getByRole('button', { name: 'İncele', exact: true }).click();
  await expect(page).toHaveURL(/days=7.*status=failed.*errorCode=timeout/);
  await expect(page.getByRole('combobox', { name: 'Durum', exact: true })).toHaveValue('failed');
  await expect(page.getByLabel('Hata türü')).toHaveValue('timeout');
  await expect(page.getByLabel('Dönem')).toHaveValue('7');
  await expect(page.getByRole('heading', { name: 'Firma bazında görünüm' })).toHaveCount(0);
  await page.getByRole('button', { name: 'Analiz', exact: true }).click();
  await expect(page.getByRole('heading', { name: 'Firma bazında görünüm' })).toBeVisible();
});

test('dashboard preserves available data when Ops is unavailable', async ({ page }) => {
  await page.route('**/ops/status', (route) => route.fulfill({ status: 503, json: { error: 'Ops erişilemiyor' } }));
  await page.goto('/');
  await expect(page.getByRole('alert')).toContainText('Ops erişilemiyor');
  await expect(page.getByRole('heading', { name: 'Öncelikli Sorunlar' })).toBeVisible();
  await expect(page.getByText('Sistem durumu doğrulanamadı', { exact: true })).toBeVisible();
});

test('company access changes require confirmation, cancellation sends nothing, Escape restores focus', async ({ page }) => {
  const mutations = [];
  page.on('request', (request) => { if (request.method() === 'DELETE') mutations.push(request.url()); });
  await page.goto('/companies/fixture-company');
  const cancel = page.getByRole('button', { name: 'İptal et', exact: true });
  await cancel.click();
  const dialog = page.getByRole('alertdialog');
  await expect(dialog).toContainText('firma kullanıcıları ürüne erişemeyecek');
  await expect(dialog.getByRole('button', { name: 'Vazgeç' })).toBeFocused();
  await page.keyboard.press('Escape');
  await expect(dialog).toHaveCount(0);
  await expect(cancel).toBeFocused();
  expect(mutations).toHaveLength(0);
  await page.getByRole('button', { name: 'Yetkiyi al' }).click();
  await expect(dialog).toContainText('fixture-user');
  await dialog.getByRole('button', { name: 'Erişimi kaldır' }).click();
  await expect.poll(() => mutations.length).toBe(1);
  expect(mutations[0]).toContain('/fixture-user/companies/fixture-company/role');
});

test('dashboard fits desktop and narrow screens', async ({ page }, testInfo) => {
  await page.goto('/');
  await expect(page.getByRole('button', { name: 'Yenile', exact: true })).toBeEnabled();
  await page.screenshot({ path: testInfo.outputPath('dashboard-desktop.png'), fullPage: true });
  await page.setViewportSize({ width: 390, height: 844 });
  await expect(page.getByRole('heading', { name: 'Genel Bakış' })).toBeVisible();
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth)).toBe(true);
  await page.screenshot({ path: testInfo.outputPath('dashboard-mobile.png'), fullPage: true });
});

test('infrastructure and auto-heal changes wait for operator confirmation', async ({ page }) => {
  await page.route('**/ops/status', (route) => route.fulfill({ json: {
    checkedAt: Date.now(), services: [], docker: { available: true }, operation: { active: null, last: null }, events: [],
    autoHeal: { enabled: false, failureThreshold: 2, maxAttempts: 3, cooldownSeconds: 30 }, emailAlertsEnabled: false,
  } }));
  const mutations = [];
  await page.route('**/ops/infrastructure/up', (route) => { mutations.push('infrastructure'); return route.fulfill({ json: { ok: true } }); });
  await page.route('**/ops/settings/autoheal', (route) => { mutations.push('autoheal'); return route.fulfill({ json: { ok: true } }); });
  await page.goto('/services');
  await page.getByRole('button', { name: 'Altyapıyı ayağa kaldır', exact: true }).click();
  const dialog = page.getByRole('alertdialog');
  await expect(dialog).toContainText('ortak altyapıyı etkiler');
  expect(mutations).toHaveLength(0);
  await dialog.getByRole('button', { name: 'Vazgeç' }).click();
  expect(mutations).toHaveLength(0);
  await page.getByRole('button', { name: 'Kaydet', exact: true }).click();
  await expect(dialog).toContainText('Otomatik onarma kapatılacak');
  expect(mutations).toHaveLength(0);
  await dialog.getByRole('button', { name: 'Ayarları uygula' }).click();
  await expect.poll(() => mutations).toEqual(['autoheal']);
});

test('retention changes explain cleanup impact before sending the update', async ({ page }) => {
  const settings = { retentionDays: 30, repeatedErrorThreshold: 2, slowQuerySeconds: 10 };
  let writes = 0;
  await page.route('**/data-analysis/api/admin/diagnostics/settings', (route) => {
    if (route.request().method() === 'PUT') writes += 1;
    return route.fulfill({ json: settings });
  });
  await page.route('**/data-analysis/api/admin/diagnostics/admin-events', (route) => route.fulfill({ json: [] }));
  await page.goto('/diagnostics');
  await page.getByRole('button', { name: 'Ayarlar ve yönetim izi' }).click();
  await page.getByRole('button', { name: 'Ayarları kaydet' }).click();
  const dialog = page.getByRole('alertdialog');
  await expect(dialog).toContainText('sonraki temizlikte silinebilir');
  expect(writes).toBe(0);
  await dialog.getByRole('button', { name: 'Vazgeç' }).click();
  expect(writes).toBe(0);
});

test('benchmark comparison uses the chosen baseline run', async ({ page }) => {
  const runs = [0.9, 0.8, 0.5].map((value, index) => ({ id: `run-${index}`, label: `Koşu ${index}`, startedAt: '2026-10-01T10:00:00Z',
    metrics: [{ name: 'role.accuracy', value, unit: 'ratio', direction: 'higher' }], caseCount: 10, passedCount: value * 10, environment: {} }));
  await page.route('**/benchmark/api/scenario-runs?*', (route) => route.fulfill({ json: { items: runs } }));
  await page.route('**/benchmark/api/scenario-runs/run-*', (route) => route.fulfill({ json: { run: runs[0], cases: [] } }));
  await page.goto('/semantic');
  const row = page.getByRole('row').filter({ has: page.getByRole('cell', { name: 'Rol doğruluğu', exact: true }) });
  await expect(row.getByRole('cell').nth(2)).toHaveText('%80');
  await page.getByRole('combobox', { name: 'Karşılaştırma koşusu' }).selectOption('run-2');
  await expect(row.getByRole('cell').nth(2)).toHaveText('%50');
  await expect(row.getByRole('cell').nth(3)).toContainText('40 puan');
});
