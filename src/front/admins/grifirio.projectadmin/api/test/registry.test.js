const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const { loadRegistry } = require('../src/registry/loadRegistry');
const { loadConfig } = require('../src/config/loadConfig');

const readComposeServices = () => {
  const services = new Map();
  let currentService = null;
  for (const line of fs.readFileSync(loadConfig({}).composeFile, 'utf8').split(/\r?\n/)) {
    const serviceMatch = line.match(/^ {2}([a-z0-9._-]+):\s*$/);
    if (serviceMatch) currentService = serviceMatch[1];
    const containerMatch = line.match(/^ {4}container_name: (.+)$/);
    if (containerMatch && currentService) services.set(currentService, containerMatch[1].trim());
  }
  return services;
};

test('every registry entry maps to a real docker-compose service and container name', () => {
  const composeServices = readComposeServices();
  const { services } = loadRegistry();

  for (const service of services) {
    assert.equal(
      composeServices.get(service.composeService),
      service.container,
      `${service.id}: compose service "${service.composeService}" must have container_name "${service.container}"`,
    );
  }
});

test('registry rejects unknown dependencies and unsupported probe types', () => {
  const writeRegistry = (services) => {
    const file = path.join(require('node:os').tmpdir(), `registry-${Date.now()}-${Math.random()}.json`);
    fs.writeFileSync(file, JSON.stringify({ services }));
    return file;
  };
  const base = { id: 'a', name: 'A', probe: { type: 'tcp' }, dependsOn: [] };

  assert.throws(() => loadRegistry(writeRegistry([{ ...base, dependsOn: ['missing'] }])), /unknown/);
  assert.throws(() => loadRegistry(writeRegistry([{ ...base, probe: { type: 'shell' } }])), /unsupported probe/);
  assert.throws(() => loadRegistry(writeRegistry([base, base])), /Duplicate/);
});
