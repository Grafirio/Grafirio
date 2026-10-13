const test = require('node:test');
const assert = require('node:assert/strict');
const { createServiceMonitor } = require('../src/monitor/createServiceMonitor');
const { createFixtures } = require('./helpers');

const STOPPED = { exists: true, running: false, status: 'exited', health: null };
const RUNNING = { exists: true, running: true, status: 'running', health: null };

function createMonitor(fixtures, probeResults) {
  let clock = 0;
  const monitor = createServiceMonitor({
    registry: fixtures.registry,
    docker: fixtures.docker,
    probe: async (service) => ({ up: probeResults[service.id], latencyMs: 1, detail: null, error: probeResults[service.id] ? null : 'refused' }),
    settings: fixtures.settings,
    audit: fixtures.audit,
    notifier: fixtures.notifier,
    intervalMs: 1000,
    now: () => clock,
    logger: { error: () => {} },
  });
  return { monitor, advance: (ms) => { clock += ms; } };
}

test('alerts once after two failed checks and once on recovery', async () => {
  const fixtures = createFixtures();
  const probeResults = { db: false, api: true };
  const { monitor } = createMonitor(fixtures, probeResults);

  await monitor.checkNow();
  assert.equal(fixtures.notifications.length, 0, 'a single failure must not alert');
  await monitor.checkNow();
  await monitor.checkNow();
  assert.deepEqual(fixtures.notifications.map((event) => event.type), ['down']);

  probeResults.db = true;
  await monitor.checkNow();
  assert.deepEqual(fixtures.notifications.map((event) => event.type), ['down', 'recovered']);
});

test('public summary exposes impact text but no service names or errors', async () => {
  const fixtures = createFixtures();
  const { monitor } = createMonitor(fixtures, { db: false, api: true });
  assert.equal(monitor.publicSummary().status, 'unknown');

  await monitor.checkNow();
  const summary = monitor.publicSummary();

  assert.equal(summary.status, 'degraded');
  assert.deepEqual(summary.affectedFeatures, ['Data unavailable.']);
  assert.doesNotMatch(JSON.stringify(summary), /Database|refused|db\.container/);
});

test('auto-heal starts a stopped container after the threshold, then gives up after max attempts', async () => {
  const fixtures = createFixtures();
  fixtures.settings.updateAutoHeal({ enabled: true, failureThreshold: 2, maxAttempts: 2, cooldownSeconds: 10 });
  fixtures.docker.setContainer('db.container', STOPPED);
  const { monitor, advance } = createMonitor(fixtures, { db: false, api: true });

  await monitor.checkNow();
  assert.equal(fixtures.docker.calls.length, 0, 'below the failure threshold');
  await monitor.checkNow();
  assert.deepEqual(fixtures.docker.calls, [{ name: 'db.container', action: 'start' }]);

  await monitor.checkNow();
  assert.equal(fixtures.docker.calls.length, 1, 'cooldown not elapsed');

  advance(11_000);
  await monitor.checkNow();
  assert.equal(fixtures.docker.calls.length, 2, 'second attempt after the cooldown');

  advance(11_000);
  await monitor.checkNow();
  assert.equal(fixtures.docker.calls.length, 2, 'no third attempt');
  assert.ok(fixtures.notifications.some((event) => event.type === 'autoheal-exhausted'));
});

test('auto-heal restarts a running but unresponsive container', async () => {
  const fixtures = createFixtures();
  fixtures.settings.updateAutoHeal({ enabled: true, failureThreshold: 1 });
  fixtures.docker.setContainer('db.container', RUNNING);
  const { monitor } = createMonitor(fixtures, { db: false, api: true });

  await monitor.checkNow();

  assert.deepEqual(fixtures.docker.calls, [{ name: 'db.container', action: 'restart' }]);
});

test('auto-heal waits for dependencies and never touches manually stopped services', async () => {
  const fixtures = createFixtures();
  fixtures.settings.updateAutoHeal({ enabled: true, failureThreshold: 1 });
  fixtures.docker.setContainer('db.container', STOPPED);
  fixtures.docker.setContainer('api.container', STOPPED);
  fixtures.settings.markManuallyStopped('db');
  const { monitor } = createMonitor(fixtures, { db: false, api: false });

  await monitor.checkNow();

  assert.deepEqual(fixtures.docker.calls, [], 'db is stopped on purpose and api waits for db');
});

test('auto-heal does nothing while disabled or while Docker is unavailable', async () => {
  const fixtures = createFixtures();
  fixtures.docker.setContainer('db.container', STOPPED);
  const { monitor } = createMonitor(fixtures, { db: false, api: true });
  await monitor.checkNow();
  await monitor.checkNow();
  assert.equal(fixtures.docker.calls.length, 0);

  fixtures.settings.updateAutoHeal({ enabled: true, failureThreshold: 1 });
  fixtures.docker.available = false;
  await monitor.checkNow();
  assert.equal(fixtures.docker.calls.length, 0);
});
