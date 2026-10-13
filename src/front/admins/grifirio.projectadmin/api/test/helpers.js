const fs = require('node:fs');
const os = require('node:os');
const path = require('node:path');
const { createAuditLog } = require('../src/audit/createAuditLog');
const { createSettingsStore } = require('../src/monitor/createSettingsStore');

const createTemporaryDataDir = () => fs.mkdtempSync(path.join(os.tmpdir(), 'ops-agent-'));

const createTestRegistry = () => {
  const services = [
    { id: 'db', name: 'Database', category: 'Database', container: 'db.container', composeService: 'db', probe: { type: 'tcp' }, dependsOn: [], impact: 'Data unavailable.' },
    { id: 'api', name: 'API', category: 'Application', container: 'api.container', composeService: 'api', probe: { type: 'tcp' }, dependsOn: ['db'], impact: 'API unavailable.' },
  ];
  return { services, byId: new Map(services.map((service) => [service.id, service])) };
};

/** Fake Docker client recording every action; container state is driven by the test. */
const createFakeDocker = () => {
  const calls = [];
  const containers = new Map();
  return {
    calls,
    available: true,
    setContainer(name, state) { containers.set(name, state); },
    isAvailable() { return Promise.resolve(this.available); },
    getContainerState: async (name) => containers.get(name) ?? { exists: false, running: false, status: 'missing', health: null },
    applyAction: async (name, action) => { calls.push({ name, action }); },
  };
};

const createFixtures = () => {
  const dataDir = createTemporaryDataDir();
  const notifications = [];
  return {
    dataDir,
    notifications,
    registry: createTestRegistry(),
    docker: createFakeDocker(),
    settings: createSettingsStore(dataDir),
    audit: createAuditLog(dataDir),
    notifier: { notify: async (event) => { notifications.push(event); } },
  };
};

module.exports = { createFixtures };
