import test from 'node:test';
import assert from 'node:assert/strict';
import { planChecks } from './plan.mjs';

test('documentation does not demand application tests', () => {
  assert.deepEqual(planChecks(['docs/guide.md', 'AGENTS.md']), { dotnet: false, python: false, frontends: [], ops: false, browser: false });
});
test('analysis contract changes cover both engines and the consuming browser', () => {
  const plan = planChecks(['src/services/Grafirio.DataAnalysis.Api/Features/Agent/AgentQueryEndpoints.cs']);
  assert.ok(plan.dotnet && plan.python && plan.browser);
});
test('shared frontend changes cover every app', () => {
  assert.equal(planChecks(['src/front/shared/theme.css']).frontends.length, 3);
});
test('pinned package updates check all .NET consumers', () => assert.ok(planChecks(['shared-packages.ref']).dotnet));
test('quality infrastructure cannot skip its own suites', () => {
  const plan = planChecks(['.github/workflows/quality.yml']);
  assert.ok(plan.dotnet && plan.python && plan.browser && plan.ops);
  assert.equal(plan.frontends.length, 3);
});
test('manual full verification and Windows paths are supported', () => {
  assert.ok(planChecks(['src\\shared\\Grafirio.QueryPolicy\\QueryPolicy.cs']).python);
  assert.equal(planChecks([], true).frontends.length, 3);
});
