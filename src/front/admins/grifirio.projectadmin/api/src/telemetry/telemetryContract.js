const { OpsError } = require('../OpsError');
const DAY = 86400000;
const SERVICES = ['gateway', 'identity', 'commerce', 'data-analysis', 'python', 'bridge'];
const NAMES = new Set(['application.log', 'activity', 'metric', 'http.request', 'bridge.connected', 'bridge.disconnected',
  'bridge.query.started', 'bridge.query.completed', 'bridge.query.failed', 'bridge.dispatch', 'query.correlation', 'python.job.started', 'python.job.completed', 'python.job.failed']);
const METRIC_TAGS = new Set(['http.request.method', 'http.response.status_code', 'error.type', 'db.system', 'db.system.name',
  'llm.operation', 'llm.outcome', 'llm.token.type', 'gen_ai.request.model', 'datasource.route', 'datasource.method', 'datasource.outcome',
  'question.outcome', 'query.status', 'analysis.outcome', 'feedback.rating', 'gc.generation', 'gc.heap.generation', 'gc.reason', 'gc.type', 'thread.state']);
const identifier = (value, max = 100) => typeof value === 'string' && new RegExp(`^[a-zA-Z0-9_.:/+-]{1,${max}}$`).test(value) ? value : null;
const guid = (value) => typeof value === 'string' && /^(?:[a-fA-F0-9]{32}|[a-fA-F0-9]{8}(?:-[a-fA-F0-9]{4}){3}-[a-fA-F0-9]{12})$/.test(value) ? value.toLowerCase().replaceAll('-', '') : null;
const finite = (value) => typeof value === 'number' && Number.isFinite(value) ? value : null;
const integer = (value, min, max) => Number.isSafeInteger(value) && value >= min && value <= max;

// No message bodies, SQL, exception messages, HTTP URLs or arbitrary attributes enter this archive.
function normalizeEvent(event, allowedServices, now) {
  if (!event || !allowedServices.includes(event.service) || !NAMES.has(event.name)
    || !['log', 'trace', 'metric'].includes(event.kind) || !guid(event.id) || !identifier(event.instanceId)
    || !identifier(event.environment) || !identifier(event.version, 160)) throw new OpsError(400, 'Invalid telemetry envelope');
  const timestamp = Date.parse(event.timestamp);
  if (!Number.isFinite(timestamp) || timestamp < now - 30 * DAY || timestamp > now + 300000) throw new OpsError(400, 'Invalid telemetry timestamp');
  const result = {
    id: guid(event.id), service: event.service, instanceId: event.instanceId, environment: event.environment, version: event.version,
    timestamp: new Date(timestamp).toISOString(), kind: event.kind, name: event.name,
    level: ['Trace', 'Debug', 'Information', 'Warning', 'Error', 'Critical'].includes(event.level) ? event.level : 'Information',
    traceId: typeof event.traceId === 'string' && /^[a-f0-9]{32}$/.test(event.traceId) ? event.traceId : null,
    spanId: typeof event.spanId === 'string' && /^[a-f0-9]{16}$/.test(event.spanId) ? event.spanId : null,
    parentSpanId: typeof event.parentSpanId === 'string' && /^[a-f0-9]{16}$/.test(event.parentSpanId) ? event.parentSpanId : null,
    diagnosticId: guid(event.diagnosticId), queryId: guid(event.queryId), requestId: guid(event.requestId),
    companyId: identifier(event.companyId), bridgeId: guid(event.bridgeId),
    category: identifier(event.category, 160), exceptionType: identifier(event.exceptionType), durationMs: finite(event.durationMs),
    status: ['Ok', 'Error', 'Unset', 'completed', 'failed', 'started'].includes(event.status) ? event.status : null,
  };
  if (event.kind === 'metric') {
    result.metric = { name: identifier(event.metric?.name, 160), unit: identifier(event.metric?.unit, 30),
      type: identifier(event.metric?.type), value: finite(event.metric?.value), count: finite(event.metric?.count),
      sum: finite(event.metric?.sum), min: finite(event.metric?.min), max: finite(event.metric?.max) };
    result.metric.tags = Object.fromEntries(Object.entries(event.metric?.tags || {}).filter(([key, value]) => METRIC_TAGS.has(key) && identifier(value, 80)).slice(0, 32));
    if (!result.metric.name) throw new OpsError(400, 'Invalid metric');
  }
  if (event.service === 'bridge' && (!result.companyId || !result.bridgeId)) throw new OpsError(400, 'Bridge identity required');
  return result;
}

module.exports = { DAY, SERVICES, identifier, guid, integer, normalizeEvent };
