const { OpsError } = require('../OpsError');

const HTTP_NOT_FOUND = 404;
const HTTP_CONFLICT = 409;
const SERVICE_ACTIONS = new Set(['start', 'stop', 'restart']);
// Default scope of "bring the infrastructure up": everything the applications depend on.
const INFRASTRUCTURE_CATEGORIES = new Set(['Database', 'Messaging', 'Identity']);

/**
 * Operator actions. Every action targets a registry id, never a raw container name or command,
 * and every action is audited with its actor.
 */
function createServiceOperations({ registry, docker, settings, audit, monitor, composeUp, startDockerDesktop, config }) {
  let activeOperation = null;
  let lastOperation = null;

  async function controlService(serviceId, action, actor) {
    try {
      const service = registry.byId.get(serviceId);
      if (!service) throw new OpsError(HTTP_NOT_FOUND, 'Unknown service');
      if (!SERVICE_ACTIONS.has(action)) throw new OpsError(HTTP_NOT_FOUND, 'Unknown action');
      if (!(await docker.isAvailable())) throw new OpsError(HTTP_CONFLICT, 'Docker is not running');

      const container = await docker.getContainerState(service.container);
      if (!container.exists) {
        throw new OpsError(HTTP_CONFLICT, 'Container does not exist; use "bring infrastructure up" to create it');
      }

      await docker.applyAction(service.container, action);
      if (action === 'stop') settings.markManuallyStopped(serviceId);
      else settings.clearManuallyStopped(serviceId);
    } catch (error) {
      audit.record({ actor, action, target: serviceId, outcome: 'failed', detail: error.message });
      throw error;
    }

    audit.record({ actor, action, target: serviceId, outcome: 'succeeded' });
    void monitor.checkNow();
  }

  function runInBackground(name, actor, task) {
    if (activeOperation) throw new OpsError(HTTP_CONFLICT, `Another operation is running: ${activeOperation.name}`);
    const operation = { name, actor, startedAt: new Date().toISOString() };
    activeOperation = operation;

    const finish = (outcome, detail) => {
      lastOperation = { ...operation, finishedAt: new Date().toISOString(), outcome, detail };
      audit.record({ actor, action: name, target: 'infrastructure', outcome, detail });
      activeOperation = null;
      void monitor.checkNow();
    };

    task().then(
      (detail) => finish('succeeded', detail),
      (error) => finish('failed', error.message),
    );
    return operation;
  }

  function startDocker(actor) {
    return runInBackground('docker-start', actor, async () => {
      await startDockerDesktop(config.dockerDesktopPath);
      return 'Docker Desktop launched; the daemon may need a minute to become ready';
    });
  }

  function bringInfrastructureUp(actor) {
    const services = registry.services.filter((service) => INFRASTRUCTURE_CATEGORIES.has(service.category));
    return runInBackground('infrastructure-up', actor, async () => {
      const { exitCode, output } = await composeUp({
        composeFile: config.composeFile,
        composeDir: config.composeDir,
        serviceNames: services.map((service) => service.composeService),
        env: config.env,
      });
      if (exitCode !== 0) throw new Error(`docker compose exited with code ${exitCode}: ${output}`);
      services.forEach((service) => settings.clearManuallyStopped(service.id));
      return 'Infrastructure is up and healthy';
    });
  }

  const getOperationState = () => ({ active: activeOperation, last: lastOperation });

  return { controlService, startDocker, bringInfrastructureUp, getOperationState };
}

module.exports = { createServiceOperations };
