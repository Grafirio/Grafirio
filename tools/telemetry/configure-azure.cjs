// Explicit operator tool. Never invoked by application startup or CI.
// Secrets remain in memory/Azure secrets; the local recovery cache uses Windows DPAPI.
const fs = require('node:fs');
const path = require('node:path');
const { execFileSync } = require('node:child_process');
const { randomBytes } = require('node:crypto');
const { createRequire } = require('node:module');
const requireApi = createRequire(path.resolve(__dirname, '../../src/front/admins/grifirio.projectadmin/api/package.json'));
const { Pool } = requireApi('pg');
const root = path.resolve(__dirname, '../..');
const cacheDir = path.join(root, '.worktree');
fs.mkdirSync(cacheDir, { recursive: true });
const cacheFile = path.join(cacheDir, 'telemetry-cloud-secrets.dpapi');
const stateFile = path.join(cacheDir, 'telemetry-live-state.json');
const subscription = '69419881-1155-43f6-81c6-1424f19a5c49';
const resourceGroup = 'grifirio-rg';
const resourceRoot = `/subscriptions/${subscription}/resourceGroups/${resourceGroup}/providers`;
const appRoot = `${resourceRoot}/Microsoft.App/containerApps`;
const postgresRoot = `${resourceRoot}/Microsoft.DBforPostgreSQL/flexibleServers/grifirio-keycloak-db`;
const dbName = 'grafirio_telemetry';
const roleName = 'grafirio_telemetry';
const imageTag = process.env.TELEMETRY_IMAGE_TAG || 'admin-20261018-pg1';
let token;
const pause = (ms) => new Promise((resolve) => setTimeout(resolve, ms));
const powershell = (command, input) => execFileSync('powershell.exe', ['-NoProfile', '-NonInteractive', '-Command', command], { input, encoding: 'utf8', windowsHide: true }).trim();
function loadSecrets() {
  if (process.platform !== 'win32') throw new Error('DPAPI cache requires Windows');
  if (fs.existsSync(cacheFile)) return JSON.parse(powershell("$ErrorActionPreference='Stop'; Add-Type -AssemblyName System.Security; [Text.Encoding]::UTF8.GetString([Security.Cryptography.ProtectedData]::Unprotect([Convert]::FromBase64String([Console]::In.ReadToEnd()), $null, [Security.Cryptography.DataProtectionScope]::CurrentUser))", fs.readFileSync(cacheFile, 'utf8')));
  const value = { databasePassword: randomBytes(32).toString('hex'),
    producers: Object.fromEntries(['gateway', 'identity', 'commerce', 'data-analysis', 'python'].map((name) => [name, randomBytes(32).toString('hex')])) };
  const encrypted = powershell("$ErrorActionPreference='Stop'; Add-Type -AssemblyName System.Security; [Convert]::ToBase64String([Security.Cryptography.ProtectedData]::Protect([Text.Encoding]::UTF8.GetBytes([Console]::In.ReadToEnd()), $null, [Security.Cryptography.DataProtectionScope]::CurrentUser))", JSON.stringify(value));
  fs.writeFileSync(cacheFile, encrypted, { flag: 'wx' });
  return value;
}
async function request(resource, method = 'GET', body, version = '2024-03-01', allow404 = false) {
  if (!token) token = powershell('az account get-access-token --resource https://management.azure.com/ --query accessToken -o tsv');
  const url = resource.startsWith('https://') ? resource : `https://management.azure.com${resource}?api-version=${version}`;
  const response = await fetch(url, { method, headers: { Authorization: `Bearer ${token}`, 'Content-Type': 'application/json' }, body: body === undefined ? undefined : JSON.stringify(body) });
  if (response.status === 404 && allow404) return null;
  const text = await response.text();
  let value; try { value = text ? JSON.parse(text) : {}; } catch { throw new Error(`Azure returned invalid JSON (${response.status})`); }
  if (!response.ok) throw new Error(`Azure operation failed (${response.status}, ${value.error?.code || 'unknown'})`);
  return value;
}
async function waitApp(name) {
  for (let count = 0; count < 120; count++) {
    const app = await request(`${appRoot}/${name}`);
    const state = app.properties.provisioningState;
    if (state === 'Succeeded' && app.properties.latestReadyRevisionName
      && app.properties.latestReadyRevisionName === app.properties.latestRevisionName) return app;
    if (['Failed', 'Canceled'].includes(state)) throw new Error(`App provisioning failed: ${name}`);
    if (count % 10 === 0) console.log(`Provisioning ${name}...`);
    await pause(3000);
  }
  throw new Error(`App provisioning timed out: ${name}`);
}
function saveState(patch) {
  const previous = fs.existsSync(stateFile) ? JSON.parse(fs.readFileSync(stateFile, 'utf8')) : {};
  fs.writeFileSync(stateFile, JSON.stringify({ ...previous, ...patch, updatedAt: new Date().toISOString() }, null, 2));
}
async function provisionDatabase(secrets, inspect = false) {
  const server = await request(postgresRoot, 'GET', undefined, '2024-08-01');
  let adminPassword;
  if (!inspect) {
    const secretResponse = await request(`${appRoot}/keycloak/listSecrets`, 'POST');
    adminPassword = secretResponse.value.find((entry) => entry.name === 'keycloak-db-admin-password')?.value;
    if (!adminPassword) throw new Error('PostgreSQL provisioning credential unavailable');
  }
  const ip = (await (await fetch('https://api.ipify.org')).text()).trim();
  if (!/^\d{1,3}(\.\d{1,3}){3}$/.test(ip)) throw new Error('Cannot determine temporary management IP');
  const rule = `${postgresRoot}/firewallRules/telemetry-setup-${randomBytes(6).toString('hex')}`;
  await request(rule, 'PUT', { properties: { startIpAddress: ip, endIpAddress: ip } }, '2024-08-01');
  let admin;
  try {
    if (!inspect) {
    admin = new Pool({ host: server.properties.fullyQualifiedDomainName, port: 5432, user: server.properties.administratorLogin,
      password: adminPassword, database: 'postgres', ssl: { rejectUnauthorized: true }, max: 1, connectionTimeoutMillis: 20000 });
    await admin.query('SELECT 1');
    const exists = (await admin.query('SELECT 1 FROM pg_database WHERE datname=$1', [dbName])).rowCount;
    const roleExists = (await admin.query('SELECT 1 FROM pg_roles WHERE rolname=$1', [roleName])).rowCount;
    if (!roleExists) await admin.query(`CREATE ROLE ${roleName} LOGIN PASSWORD '${secrets.databasePassword}' CONNECTION LIMIT 8 NOSUPERUSER NOCREATEDB NOCREATEROLE NOREPLICATION`);
    // Two revisions may overlap during a rollout (3 connections each), plus bounded verification.
    await admin.query(`ALTER ROLE ${roleName} CONNECTION LIMIT 8`);
    if (!exists) await admin.query(`CREATE DATABASE ${dbName}`);
    await admin.query(`REVOKE ALL ON DATABASE ${dbName} FROM PUBLIC`);
    await admin.query(`GRANT CONNECT ON DATABASE ${dbName} TO ${roleName}`);
    await admin.end(); admin = null;
    admin = new Pool({ host: server.properties.fullyQualifiedDomainName, port: 5432, user: server.properties.administratorLogin,
      password: adminPassword, database: dbName, ssl: { rejectUnauthorized: true }, max: 1, connectionTimeoutMillis: 20000 });
    const client = await admin.connect();
    try {
      await client.query('BEGIN');
      await client.query('REVOKE ALL ON SCHEMA public FROM PUBLIC');
      const installed = (await client.query("SELECT to_regclass('public.telemetry_schema') AS name")).rows[0].name;
      if (!installed) await client.query(fs.readFileSync(path.join(root, 'src/front/admins/grifirio.projectadmin/api/src/telemetry/postgres-schema.sql'), 'utf8'));
      await client.query(`GRANT USAGE ON SCHEMA public TO ${roleName}`);
      await client.query(`GRANT SELECT ON telemetry_schema TO ${roleName}`);
      await client.query(`GRANT SELECT,INSERT,UPDATE,DELETE ON events,meta,producers TO ${roleName}`);
      await client.query(`GRANT SELECT,INSERT ON audit TO ${roleName}`);
      await client.query(`GRANT USAGE,SELECT ON ALL SEQUENCES IN SCHEMA public TO ${roleName}`);
      await client.query('COMMIT');
    } catch (error) { await client.query('ROLLBACK'); throw error; } finally { client.release(); }
    }
    // Verify the restricted identity can open the exact schema before touching live apps.
    const url = new URL(`postgresql://${server.properties.fullyQualifiedDomainName}:5432/${dbName}`);
    url.username = roleName; url.password = secrets.databasePassword;
    const { createPostgresTelemetryArchive } = requireApi('./src/telemetry/createPostgresTelemetryArchive');
    const archive = await createPostgresTelemetryArchive(url.href);
    try {
      const status = await archive.status();
      if (inspect) {
        console.log(JSON.stringify({ storage: status.storage, count: status.count, bytes: status.bytes,
          settings: status.settings, loss: status.loss, producers: status.producers.map(({ service, instanceId, receivedAt, dropped }) => ({ service, instanceId, receivedAt, dropped })) }, null, 2));
        const state = fs.existsSync(stateFile) ? JSON.parse(fs.readFileSync(stateFile, 'utf8')) : {};
        if (inspect === 'persistence') {
          const baseline = state.persistenceBaseline;
          if (!baseline) throw new Error('Capture a historical boundary before the restart check');
          const historical = await archive.query({ from: baseline.from, until: baseline.until, snapshot: baseline.snapshot, limit: 1 });
          if (historical.items[0]?.id !== baseline.eventId) throw new Error('Previously captured archive event is missing');
          console.log('Previously captured archive event remains readable.');
        } else if (inspect === 'capture' && status.count) {
          const sample = await archive.query({ limit: 1 });
          saveState({ persistenceBaseline: { from: sample.from, until: sample.until, snapshot: sample.snapshot, eventId: sample.items[0].id } });
          console.log('Captured a historical event boundary for the collector restart check.');
        }
      }
    } finally { await archive.close(); }
    if (inspect) {
      const restricted = new Pool({ connectionString: url.href, ssl: { rejectUnauthorized: true }, max: 1 });
      try {
        const permissions = (await restricted.query("SELECT has_table_privilege('audit','DELETE') AS audit_delete, has_schema_privilege('public','CREATE') AS schema_create")).rows[0];
        if (permissions.audit_delete || permissions.schema_create) throw new Error('Runtime role has excessive database privileges');
        console.log('Runtime role cannot delete audit entries or create schema objects.');
      } finally { await restricted.end(); }
    }
    saveState({ database: dbName, databaseReady: true });
    console.log('Dedicated telemetry database and restricted runtime role verified.');
    return url.href;
  } finally {
    if (admin) await admin.end();
    await request(rule, 'DELETE', undefined, '2024-08-01');
    console.log('Temporary database management firewall rule removed.');
  }
}
async function deployCollector(secrets, databaseUrl) {
  const gateway = await request(`${appRoot}/gateway`);
  const registry = gateway.properties.configuration.registries[0];
  const registrySecrets = await request(`${appRoot}/gateway/listSecrets`, 'POST');
  const password = registrySecrets.value.find((secret) => secret.name === registry.passwordSecretRef)?.value;
  if (!password) throw new Error('Existing registry credential unavailable');
  const secretList = [{ name: 'acr-pull', value: password }, { name: 'telemetry-database', value: databaseUrl },
    ...Object.entries(secrets.producers).map(([name, value]) => ({ name: `telemetry-${name}`, value }))];
  const env = [
    { name: 'TELEMETRY_DATABASE_URL', secretRef: 'telemetry-database' },
    { name: 'OPS_OIDC_ISSUER', value: 'https://login.grafirio.com/realms/grafirio' },
    { name: 'OPS_ALLOWED_ORIGINS', value: 'https://manager.grafirio.com' },
    ...Object.keys(secrets.producers).map((name) => ({ name: `TELEMETRY_${name.toUpperCase().replaceAll('-', '_')}_KEY`, secretRef: `telemetry-${name}` })),
  ];
  const body = { location: gateway.location, tags: { purpose: 'grafirio-telemetry' }, properties: {
    managedEnvironmentId: gateway.properties.managedEnvironmentId,
    configuration: { activeRevisionsMode: 'Single', secrets: secretList,
      registries: [{ server: registry.server, username: registry.username, passwordSecretRef: 'acr-pull' }],
      ingress: { external: false, targetPort: 3001, transport: 'auto', allowInsecure: false,
        traffic: [{ latestRevision: true, weight: 100 }] } },
    template: { containers: [{ name: 'telemetry-archive', image: `grifirioacr.azurecr.io/telemetry-archive:${imageTag}`, env,
      resources: { cpu: 0.25, memory: '0.5Gi' }, probes: [
        { type: 'Readiness', httpGet: { path: '/health', port: 3001 }, periodSeconds: 30, timeoutSeconds: 10, failureThreshold: 3 },
        { type: 'Startup', httpGet: { path: '/health', port: 3001 }, periodSeconds: 10, timeoutSeconds: 10, failureThreshold: 18 },
      ] }], scale: { minReplicas: 1, maxReplicas: 1 } },
  } };
  const existing = await request(`${appRoot}/telemetry-archive`, 'GET', undefined, '2024-03-01', true);
  if (existing && existing.tags?.purpose !== 'grafirio-telemetry') throw new Error('Collector name belongs to an unrelated resource');
  await request(`${appRoot}/telemetry-archive`, 'PUT', body);
  const app = await waitApp('telemetry-archive');
  saveState({ collector: app.name, collectorFqdn: app.properties.configuration.ingress.fqdn, collectorImage: `telemetry-archive:${imageTag}` });
  console.log('Internal HTTPS collector provisioned.');
  return app;
}
async function updateProducer(name, secrets, endpoint) {
  const service = { 'identity-api': 'identity', 'commerce-api': 'commerce', 'data-analysis-api': 'data-analysis', gateway: 'gateway', 'pycaret-engine': 'python' }[name];
  const app = await request(`${appRoot}/${name}`);
  const before = { image: app.properties.template.containers[0].image, revision: app.properties.latestReadyRevisionName };
  const currentSecrets = await request(`${appRoot}/${name}/listSecrets`, 'POST');
  const secretList = currentSecrets.value.filter((entry) => entry.name !== 'telemetry-producer').map(({ name: key, value, keyVaultUrl, identity }) => keyVaultUrl ? { name: key, keyVaultUrl, identity } : { name: key, value });
  secretList.push({ name: 'telemetry-producer', value: secrets.producers[service] });
  const container = app.properties.template.containers[0];
  const patchEnv = service === 'python' ? [
    { name: 'TELEMETRY_ARCHIVE_ENDPOINT', value: `${endpoint}/api/telemetry/ingest` },
    { name: 'TELEMETRY_ARCHIVE_KEY', secretRef: 'telemetry-producer' },
    { name: 'ENVIRONMENT', value: 'Production' },
    { name: 'GRAFIRIO_BUILD_SHA', value: imageTag },
  ] : [
    { name: 'TelemetryArchive__Endpoint', value: `${endpoint}/api/telemetry/ingest` },
    { name: 'TelemetryArchive__Key', secretRef: 'telemetry-producer' },
  ];
  if (name === 'gateway') patchEnv.push({ name: 'ReverseProxy__Clusters__telemetry.cluster__Destinations__destination1__Address', value: endpoint });
  const removeNames = new Set([...patchEnv.map((entry) => entry.name), 'TELEMETRY_ALLOW_INSECURE_TRANSPORT', 'TelemetryArchive__AllowInsecureTransport']);
  container.env = [...(container.env || []).filter((entry) => !removeNames.has(entry.name)), ...patchEnv];
  container.image = `grifirioacr.azurecr.io/${name}:${imageTag}`;
  // Preserve all existing ingress, identities, scale, probes and unrelated secrets/settings.
  await request(`${appRoot}/${name}`, 'PATCH', { properties: { configuration: { ...app.properties.configuration, secrets: secretList }, template: app.properties.template } });
  const updated = await waitApp(name);
  if (updated.properties.latestReadyRevisionName !== updated.properties.latestRevisionName) throw new Error(`New producer revision not ready: ${name}`);
  const state = fs.existsSync(stateFile) ? JSON.parse(fs.readFileSync(stateFile, 'utf8')) : {};
  saveState({ producers: { ...state.producers, [name]: { before, image: container.image, revision: updated.properties.latestReadyRevisionName } } });
  console.log(`HTTPS producer configured and ready: ${name}`);
}
async function main() {
  const command = process.argv[2];
  if (!['database', 'collector', 'producer', 'panel', 'verify', 'capture', 'check-persistence'].includes(command)) throw new Error('Choose database, collector, producer <app>, panel, verify, capture or check-persistence');
  const secrets = loadSecrets();
  if (command === 'database') { await provisionDatabase(secrets); return; }
  if (command === 'verify') { await provisionDatabase(secrets, true); return; }
  if (command === 'capture') { await provisionDatabase(secrets, 'capture'); return; }
  if (command === 'check-persistence') { await provisionDatabase(secrets, 'persistence'); return; }
  if (command === 'collector') {
    const databaseUrl = await provisionDatabase(secrets);
    await deployCollector(secrets, databaseUrl); return;
  }
  const collector = await request(`${appRoot}/telemetry-archive`);
  if (collector.properties.configuration.ingress.allowInsecure || collector.properties.configuration.ingress.external) throw new Error('Collector must use internal HTTPS');
  if (command === 'producer') {
    const name = process.argv[3];
    if (!['gateway', 'identity-api', 'commerce-api', 'data-analysis-api', 'pycaret-engine'].includes(name)) throw new Error('Unknown producer');
    await updateProducer(name, secrets, `https://${collector.properties.configuration.ingress.fqdn}`); return;
  }
  const app = await request(`${appRoot}/projectadmin`);
  app.properties.template.containers[0].image = `grifirioacr.azurecr.io/projectadmin:${imageTag}`;
  await request(`${appRoot}/projectadmin`, 'PATCH', { properties: { template: app.properties.template } });
  const updated = await waitApp('projectadmin');
  if (updated.properties.latestReadyRevisionName !== updated.properties.latestRevisionName) throw new Error('Panel revision not ready');
  saveState({ panelRevision: updated.properties.latestReadyRevisionName });
  console.log('Admin panel revision ready.');
}
main().catch((error) => { console.error(`Telemetry setup stopped: ${error.code || error.message?.replace(/postgresql:\/\/\S+/g, '[redacted]') || 'unknown error'}`); process.exitCode = 1; });
