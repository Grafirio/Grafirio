import { test, expect } from '@playwright/test';
const diagnosticId = '11111111-1111-4111-8111-111111111111';
const settings = { retentionDays: 7, maxRows: 50000, maxBytes: 67108864 };

test.beforeEach(async ({ page, baseURL }) => {
  await page.addInitScript(() => { window.__grafirioTestRole = 'admin'; });
  await page.route('**/*', (route) => {
    const url = new URL(route.request().url());
    if (url.origin !== baseURL) return route.abort('blockedbyclient');
    if (url.pathname === '/admin/telemetry/status') return route.fulfill({ json: {
      count: 51, bytes: 50000, loss: { expired: 2, evicted: 1 }, settings,
      producers: [{ producer: 'python', service: 'python', instanceId: 'fixture-replica', receivedAt: new Date().toISOString(), dropped: 3 }],
    } });
    if (url.pathname === '/admin/telemetry/events') {
      const next = url.searchParams.get('page') === '2';
      return route.fulfill({ json: { snapshot: 51, page: next ? 2 : 1, total: 51, limit: 50,
        items: [{ sequence: next ? 1 : 51, name: 'python.job.completed', service: 'python', instanceId: 'fixture-replica',
          timestamp: new Date().toISOString(), environment: 'Testing', version: 'fixture', level: 'Information', status: 'completed',
          diagnosticId, traceId: 'a'.repeat(32), requestId: null, kind: 'log' }],
      } });
    }
    if (url.pathname.startsWith('/admin/telemetry/')) return route.fulfill({ status: 503, json: { error: 'Sentetik arşiv kesintisi' } });
    return route.continue();
  });
});

test('archive deep link and pagination preserve query, service and snapshot scope', async ({ page }, testInfo) => {
  await page.goto(`/observability?source=archive&diagnosticId=${diagnosticId}`);
  await expect(page.getByRole('heading', { name: 'Kalıcı Telemetri Arşivi' })).toBeVisible();
  await expect(page.getByRole('cell', { name: 'Python işi tamamlandı' })).toBeVisible();
  await expect(page.getByLabel('Takip numarası', { exact: true })).toHaveValue(diagnosticId);
  await page.getByRole('combobox', { name: 'Arşiv servisi' }).selectOption('python');
  await expect(page.getByRole('cell', { name: 'Python işi tamamlandı' })).toBeVisible();
  const request = page.waitForRequest((value) => {
    const url = new URL(value.url());
    return url.pathname === '/admin/telemetry/events' && url.searchParams.get('page') === '2'
      && url.searchParams.get('snapshot') === '51' && url.searchParams.get('service') === 'python'
      && url.searchParams.get('diagnosticId') === diagnosticId;
  });
  await page.getByRole('button', { name: 'Sonraki arşiv sayfası' }).click();
  await request;
  await expect(page.getByText('51 kayıt · Sayfa 2', { exact: true })).toBeVisible();
  await expect(page.getByRole('link', { name: 'Sorgu ayrıntısı →' })).toHaveAttribute('href', `/diagnostics?id=${diagnosticId}`);
  await page.evaluate(() => window.scrollTo(0, 0));
  await page.screenshot({ path: testInfo.outputPath('telemetry-archive-desktop.png'), fullPage: true });
  await page.setViewportSize({ width: 390, height: 844 });
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true);
});

test('archive outage is shown as a failure rather than an empty successful result', async ({ page }) => {
  await page.route('**/admin/telemetry/events?*', (route) => route.fulfill({ status: 503, json: { error: 'Sentetik arşiv kesintisi' } }));
  await page.goto('/observability?source=archive');
  await expect(page.getByRole('alert')).toContainText('Sentetik arşiv kesintisi');
  await expect(page.getByText('Bu filtrelerde arşiv kaydı yok.', { exact: true })).toHaveCount(0);
});

test('an HTML fallback cannot masquerade as a successful archive response', async ({ page }) => {
  await page.route('**/admin/telemetry/events?*', (route) => route.fulfill({ status: 200, contentType: 'text/html', body: '<html>Fallback</html>' }));
  await page.goto('/observability?source=archive');
  await expect(page.getByRole('alert')).toContainText('Arşiv liste yanıtı geçersiz');
  await expect(page.getByText('Bu filtrelerde arşiv kaydı yok.', { exact: true })).toHaveCount(0);
});

test('retention changes are sent only after explicit confirmation', async ({ page }) => {
  const writes = [];
  await page.route('**/admin/telemetry/settings', (route) => {
    writes.push(route.request().postDataJSON());
    return route.fulfill({ json: writes.at(-1) });
  });
  await page.goto('/observability?source=archive');
  await page.getByText('Saklama ve hacim politikası', { exact: true }).click();
  await page.getByLabel('Saklama süresi (gün)').fill('3');
  await page.getByRole('button', { name: 'Arşiv politikasını kaydet' }).click();
  const dialog = page.getByRole('alertdialog');
  await expect(dialog).toContainText('kalıcı olarak silinebilir');
  expect(writes).toHaveLength(0);
  await dialog.getByRole('button', { name: 'Vazgeç' }).click();
  expect(writes).toHaveLength(0);
  await page.getByRole('button', { name: 'Arşiv politikasını kaydet' }).click();
  await dialog.getByRole('button', { name: 'Politikayı uygula' }).click();
  await expect.poll(() => writes.length).toBe(1);
  expect(writes[0].retentionDays).toBe(3);
});
