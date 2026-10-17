const { OpsError } = require('../OpsError');
const { DAY, identifier, guid, integer } = require('./telemetryContract');
function buildTelemetryQuery(filters, { now, snapshot, linksByField = {} }) {
    const clauses = []; const args = [];
    snapshot = Number(filters.snapshot ?? snapshot);
    if (!integer(snapshot, 0, Number.MAX_SAFE_INTEGER)) throw new OpsError(400, 'Invalid archive snapshot');
    clauses.push('seq<=?'); args.push(snapshot);
    const from = filters.from ? Date.parse(filters.from) : now() - DAY;
    const until = filters.until ? Date.parse(filters.until) : now();
    if (!Number.isFinite(from) || !Number.isFinite(until) || from > until || until - from > 30 * DAY) throw new OpsError(400, 'Invalid time range (maximum 30 days)');
    clauses.push('timestamp>=? AND timestamp<=?'); args.push(new Date(from).toISOString(), new Date(until).toISOString());
    for (const field of ['service', 'instanceId', 'kind', 'level', 'companyId', 'bridgeId', 'requestId', 'environment', 'version']) {
      if (filters[field]) {
        if (!identifier(filters[field], 160)) throw new OpsError(400, 'Invalid filter');
        const column = ['environment', 'version'].includes(field) ? `json_extract(body,'$.${field}')` : field;
        clauses.push(`${column}=?`); args.push(['bridgeId', 'requestId'].includes(field) ? guid(filters[field]) : filters[field]);
      }
    }
    // Link edge events only through a server-issued dispatch with the SAME company/Bridge/request.
    for (const field of ['traceId', 'diagnosticId', 'queryId']) {
      if (!filters[field]) continue;
      const value = field === 'traceId' ? filters[field] : guid(filters[field]);
      if (!value || (field === 'traceId' && !/^[a-f0-9]{32}$/.test(value))) throw new OpsError(400, 'Invalid correlation id');
      const links = field === 'traceId' ? [] : (linksByField[field] || []);
      const traceIds = [...new Set(links.map((row) => row.traceId).filter(Boolean))].slice(0, 100);
      const queryIds = [...new Set(links.map((row) => row.queryId).filter(Boolean))].slice(0, 100);
      const match = (alias) => {
        const tests = [`${alias}.${field}=?`]; const parameters = [value];
        if (traceIds.length) { tests.push(`${alias}.traceId IN (${traceIds.map(() => '?').join(',')})`); parameters.push(...traceIds); }
        if (queryIds.length) { tests.push(`${alias}.queryId IN (${queryIds.map(() => '?').join(',')})`); parameters.push(...queryIds); }
        return { sql: `(${tests.join(' OR ')})`, parameters };
      };
      const direct = match('events'), trusted = match('trusted');
      clauses.push(`((service!='bridge' AND ${direct.sql}) OR (service='bridge' AND EXISTS(SELECT 1 FROM events trusted WHERE trusted.service='data-analysis'
        AND json_extract(trusted.body,'$.name')='bridge.dispatch' AND ${trusted.sql}
        AND trusted.requestId=events.requestId AND trusted.companyId=events.companyId AND trusted.bridgeId=events.bridgeId)))`);
      args.push(...direct.parameters, ...trusted.parameters);
    }
    const page = Number(filters.page ?? 1), limit = Number(filters.limit ?? 50);
    if (!integer(page, 1, 1000000) || !integer(limit, 1, 200)) throw new OpsError(400, 'Invalid pagination');
    const where = clauses.join(' AND ');
    return { where, args, snapshot, from, until, page, limit };
}
module.exports = { buildTelemetryQuery };
