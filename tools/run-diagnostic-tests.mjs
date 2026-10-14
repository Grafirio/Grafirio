// Deployment checks for saved production translation examples. Never executes customer SQL.
import { writeFile } from 'node:fs/promises';

const base = process.env.GRAFIRIO_GATEWAY_URL?.replace(/\/$/, '');
const token = process.env.GRAFIRIO_MANAGER_TOKEN;
if (!base || !token) throw new Error('GRAFIRIO_GATEWAY_URL and GRAFIRIO_MANAGER_TOKEN are required. Use a platform admin token.');
const headers = { Authorization: `Bearer ${token}`, 'Content-Type': 'application/json' };
const endpoint = `${base}/data-analysis/api/admin/diagnostics`;
async function request(path, method = 'GET') {
  const response = await fetch(`${endpoint}${path}`, { method, headers, signal: AbortSignal.timeout(660000) });
  if (!response.ok) throw new Error(`Diagnostic check returned HTTP ${response.status}.`);
  return response.json();
}
const tests = await request('/tests');
if (tests.length >= 200) throw new Error('The manager test list reached its 200-case limit. Split/archive obsolete examples before using this deployment gate.');
const results = [];
for (const test of tests) {
  try {
    const run = await request(`/tests/${test.id}/run`, 'POST');
    // Reports contain outcome and mismatching fields, rather than customer data or model responses.
    results.push({ id: test.id, name: test.name, passed: run.passed, differences: run.differences,
      at: run.at, model: run.model, promptVersion: run.promptVersion });
  } catch (error) { results.push({ id: test.id, passed: false, error: error.message }); }
}
const report = { createdAt: new Date().toISOString(), count: results.length, passed: results.filter((r) => r.passed).length, results };
await writeFile(process.env.GRAFIRIO_DIAGNOSTIC_REPORT || 'diagnostic-check-report.json', JSON.stringify(report, null, 2));
console.log(`${report.passed}/${report.count} diagnostic examples passed.`);
if (!results.length || results.some((r) => !r.passed)) process.exitCode = 1;
