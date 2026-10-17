import { test, expect } from '@playwright/test';

const metrics = { total: 1, completed: 1, failed: 0, emptyResults: 0, successRate: 1,
  latency: { averageMs: 100, p95Ms: 100, sampleCount: 1, missingCount: 0 } };
function response(search, page = '1') {
  const empty = search === 'no-match';
  const question = search === 'paginate' ? `Sayfa ${page} sentetik sorgu` : 'Sentetik ciro sorgusu';
  return {
    items: empty ? [] : [{ id: 'synthetic-query', question, status: 'completed', reviewStatus: 'new', createdAt: '2026-10-01T10:00:00Z', durationMs: 100 }],
    total: empty ? 0 : search === 'paginate' ? 51 : 1,
    summary: { ...metrics, negativeFeedback: 0, ratedCount: 0, errors: [],
      visibility: { ...metrics, executionJobsCreated: 1, statusDistribution: [{ status: 'completed', count: 1 }],
        byCompany: [], byConnection: [], reviewCandidates: [], reviewCandidateCount: 0, reviewCandidateLimit: 20 },
      comparison: { available: false } },
  };
}

test.beforeEach(async ({ page, baseURL }) => {
  // Fail closed: no test may contact a real API, identity server or customer host.
  await page.route('**/*', async (route) => {
    const url = new URL(route.request().url());
    if (url.origin !== baseURL) return route.abort('blockedbyclient');
    if (url.pathname === '/data-analysis/api/admin/diagnostics') {
      return route.fulfill({ json: response(url.searchParams.get('search'), url.searchParams.get('page') || '1') });
    }
    if (url.pathname === '/data-analysis/api/admin/diagnostics/summary') {
      return route.fulfill({ json: response('').summary });
    }
    if (url.pathname === '/data-analysis/api/admin/diagnostics/synthetic-query') {
      return route.fulfill({ json: { ...response('').items[0], companyId: 'synthetic-company', connectionId: 'synthetic-connection',
        stage: 'validation', reviewNote: '', parameters: {}, audit: {}, context: {}, events: [], legacy: false } });
    }
    if (url.pathname.startsWith('/data-analysis/')) return route.fulfill({ status: 500, json: { message: 'Unexpected fixture API' } });
    return route.continue();
  });
});

test('anonymous visitor must sign in', async ({ page }) => {
  await page.goto('/diagnostics');
  await expect(page.getByRole('heading', { name: 'Oturum gerekli' })).toBeVisible();
  await expect(page.getByRole('navigation')).toHaveCount(0);
});

test('company role cannot see platform navigation', async ({ page }) => {
  await page.addInitScript(() => { window.__grafirioTestRole = 'company'; });
  await page.goto('/diagnostics');
  await expect(page.getByRole('heading', { name: 'Bu panele erişim yetkiniz yok' })).toBeVisible();
  await expect(page.getByRole('navigation')).toHaveCount(0);
});

test('admin searches diagnostics and sees the empty result state', async ({ page }) => {
  await page.addInitScript(() => { window.__grafirioTestRole = 'admin'; });
  await page.goto('/diagnostics');
  await expect(page.getByRole('button', { name: 'Sentetik ciro sorgusu', exact: true })).toBeVisible();
  const request = page.waitForRequest((r) => new URL(r.url()).searchParams.get('search') === 'no-match');
  await page.getByLabel('Soru veya takip numarası').fill('no-match');
  await request;
  await expect(page.getByText('Bu filtrelere uygun kayıt yok.', { exact: true })).toBeVisible();
  await expect(page.getByRole('button', { name: 'Sentetik ciro sorgusu', exact: true })).toHaveCount(0);
});

test('API failure is visible instead of presenting empty success', async ({ page }) => {
  await page.addInitScript(() => { window.__grafirioTestRole = 'admin'; });
  await page.route('**/data-analysis/api/admin/diagnostics?*', (route) => route.fulfill({ status: 503, json: { message: 'Sentetik servis kesintisi' } }));
  await page.goto('/diagnostics');
  await expect(page.locator('.dg-error')).toBeVisible();
  await expect(page.getByText('Bu filtrelere uygun kayıt yok.', { exact: true })).toHaveCount(0);
});

test('pagination requests the next page and keeps the search filter', async ({ page }) => {
  await page.addInitScript(() => { window.__grafirioTestRole = 'admin'; });
  await page.goto('/diagnostics');
  await page.getByLabel('Soru veya takip numarası').fill('paginate');
  await expect(page.getByRole('button', { name: 'Sayfa 1 sentetik sorgu', exact: true })).toBeVisible();
  const next = page.waitForRequest((r) => {
    const url = new URL(r.url());
    return url.pathname.endsWith('/diagnostics') && url.searchParams.get('page') === '2' && url.searchParams.get('search') === 'paginate';
  });
  await page.getByRole('button', { name: 'Sonraki', exact: true }).click();
  await next;
  await expect(page.getByRole('button', { name: 'Sayfa 2 sentetik sorgu', exact: true })).toBeVisible();
  await expect(page.getByRole('button', { name: 'Sonraki', exact: true })).toBeDisabled();
});

test('opening a diagnostic preserves its trace id in the URL', async ({ page }) => {
  await page.addInitScript(() => { window.__grafirioTestRole = 'admin'; });
  await page.goto('/diagnostics');
  await page.getByRole('button', { name: 'Sentetik ciro sorgusu', exact: true }).click();
  await expect(page).toHaveURL(/\?id=synthetic-query$/);
  await expect(page.getByRole('heading', { name: 'Sorgu ayrıntısı', exact: true })).toBeVisible();
  await expect(page.locator('dd').filter({ hasText: /^synthetic-query$/ })).toBeVisible();
});
