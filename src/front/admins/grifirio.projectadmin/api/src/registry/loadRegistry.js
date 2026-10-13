const fs = require('node:fs');
const path = require('node:path');

const ALLOWED_PROBE_TYPES = new Set(['tcp', 'http', 'postgres', 'mongo', 'redis']);
const REGISTRY_PATH = path.join(__dirname, '..', '..', 'registry.json');

/** Reads and validates the service registry; the registry is the only source of operable service ids. */
function loadRegistry(registryPath = REGISTRY_PATH) {
  const { services } = JSON.parse(fs.readFileSync(registryPath, 'utf8'));
  const byId = new Map();

  for (const service of services) {
    if (byId.has(service.id)) throw new Error(`Duplicate service id: ${service.id}`);
    if (!ALLOWED_PROBE_TYPES.has(service.probe?.type)) {
      throw new Error(`Service ${service.id} has an unsupported probe type`);
    }
    byId.set(service.id, service);
  }

  for (const service of services) {
    for (const dependency of service.dependsOn) {
      if (!byId.has(dependency)) throw new Error(`Service ${service.id} depends on unknown ${dependency}`);
    }
  }

  return { services, byId };
}

module.exports = { loadRegistry };
