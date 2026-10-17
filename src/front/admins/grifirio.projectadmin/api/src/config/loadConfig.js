const path = require('node:path');

const DEFAULT_PORT = 3001;
const DEFAULT_MONITOR_INTERVAL_MS = 15_000;
const DEFAULT_PROBE_TIMEOUT_MS = 3_000;
const DEFAULT_ALERT_MIN_INTERVAL_MS = 10 * 60_000;
const DEFAULT_SMTP_PORT = 587;
// api/src/config -> repository root
const REPOSITORY_ROOT_DEPTH = 7;

const positiveNumber = (value, fallback) => {
  const parsed = Number(value);
  return Number.isFinite(parsed) && parsed > 0 ? parsed : fallback;
};

const splitList = (value, fallback) =>
  value ? value.split(',').map((item) => item.trim()).filter(Boolean) : fallback;

function loadConfig(env = process.env) {
  const repositoryRoot = path.resolve(__dirname, ...Array(REPOSITORY_ROOT_DEPTH).fill('..'));
  const composeDir = env.OPS_COMPOSE_DIR || repositoryRoot;
  const smtp = {
    host: env.OPS_SMTP_HOST,
    port: positiveNumber(env.OPS_SMTP_PORT, DEFAULT_SMTP_PORT),
    user: env.OPS_SMTP_USER,
    password: env.OPS_SMTP_PASSWORD,
    from: env.OPS_SMTP_FROM,
    recipients: splitList(env.OPS_ALERT_RECIPIENTS, []),
  };

  return {
    port: positiveNumber(env.OPS_PORT, DEFAULT_PORT),
    // Loopback by default; a container reaching the host needs an explicit wider bind.
    bindHost: env.OPS_BIND_HOST || '127.0.0.1',
    allowedOrigins: splitList(env.OPS_ALLOWED_ORIGINS, ['http://localhost:59265', 'http://localhost:59666']),
    oidcIssuer: env.OPS_OIDC_ISSUER || 'http://localhost:8080/realms/grafirio',
    oidcJwksUrl: env.OPS_OIDC_JWKS_URL || null,
    platformAdminRole: 'PLATFORM_ADMIN',
    rolesClaim: 'business_roles',
    recoveryKey: env.OPS_RECOVERY_KEY || null,
    telemetryEnabled: env.TELEMETRY_ARCHIVE_ENABLED === 'true',
    telemetryProducers: ['gateway', 'identity', 'commerce', 'data-analysis', 'python'].map((service) => ({
      id: service, key: env[`TELEMETRY_${service.toUpperCase().replaceAll('-', '_')}_KEY`],
      services: service === 'data-analysis' ? [service, 'bridge'] : [service],
    })).filter((producer) => producer.key),
    composeDir,
    composeFile: path.join(composeDir, 'docker-compose.yml'),
    dockerDesktopPath: env.OPS_DOCKER_DESKTOP_PATH || 'C:\\Program Files\\Docker\\Docker\\Docker Desktop.exe',
    dataDir: env.OPS_DATA_DIR || path.join(__dirname, '..', '..', 'data'),
    monitorIntervalMs: positiveNumber(env.OPS_MONITOR_INTERVAL_MS, DEFAULT_MONITOR_INTERVAL_MS),
    probeTimeoutMs: positiveNumber(env.OPS_PROBE_TIMEOUT_MS, DEFAULT_PROBE_TIMEOUT_MS),
    alertMinIntervalMs: positiveNumber(env.OPS_ALERT_MIN_INTERVAL_MS, DEFAULT_ALERT_MIN_INTERVAL_MS),
    smtp: { ...smtp, enabled: Boolean(smtp.host && smtp.from && smtp.recipients.length) },
    env,
  };
}

module.exports = { loadConfig };
