const { loadConfig } = require('./src/config/loadConfig');
const { loadRegistry } = require('./src/registry/loadRegistry');
const { createDockerClient } = require('./src/docker/dockerClient');
const { composeUp } = require('./src/docker/composeUp');
const { startDockerDesktop } = require('./src/docker/startDockerDesktop');
const { probeService } = require('./src/probes/probeService');
const { createTokenVerifier } = require('./src/auth/createTokenVerifier');
const { createRecoveryGuard } = require('./src/auth/createRecoveryGuard');
const { createAuditLog } = require('./src/audit/createAuditLog');
const { createSettingsStore } = require('./src/monitor/createSettingsStore');
const { createServiceMonitor } = require('./src/monitor/createServiceMonitor');
const { createEmailNotifier } = require('./src/alerts/createEmailNotifier');
const { createServiceOperations } = require('./src/operations/createServiceOperations');
const { createApp } = require('./src/http/createApp');

const config = loadConfig();
const registry = loadRegistry();
const docker = createDockerClient();
const settings = createSettingsStore(config.dataDir);
const audit = createAuditLog(config.dataDir);
const notifier = createEmailNotifier({ smtp: config.smtp, minIntervalMs: config.alertMinIntervalMs });

const monitor = createServiceMonitor({
  registry,
  docker,
  probe: (service) => probeService(service, { timeoutMs: config.probeTimeoutMs, env: config.env }),
  settings,
  audit,
  notifier,
  intervalMs: config.monitorIntervalMs,
});
const operations = createServiceOperations({
  registry, docker, settings, audit, monitor, composeUp, startDockerDesktop, config,
});

const app = createApp({
  config,
  monitor,
  operations,
  settings,
  audit,
  verifyToken: createTokenVerifier(config),
  recoveryGuard: createRecoveryGuard({ recoveryKey: config.recoveryKey }),
});

app.listen(config.port, config.bindHost, () => {
  console.info(`Ops agent listening on http://${config.bindHost}:${config.port}`);
  if (!config.recoveryKey) console.warn('OPS_RECOVERY_KEY is not set: the /recovery actions are disabled');
  monitor.start();
});
