const fs = require('node:fs');
const path = require('node:path');

const AUDIT_FILE_NAME = 'audit.jsonl';
const DEFAULT_READ_LIMIT = 100;

/** Append-only JSON Lines audit trail: who did what to which service, and the outcome. */
function createAuditLog(dataDir) {
  const filePath = path.join(dataDir, AUDIT_FILE_NAME);
  fs.mkdirSync(dataDir, { recursive: true });

  function record({ actor, action, target, outcome, detail = null }) {
    const entry = { at: new Date().toISOString(), actor, action, target, outcome, detail };
    fs.appendFileSync(filePath, `${JSON.stringify(entry)}\n`);
    return entry;
  }

  function readRecent(limit = DEFAULT_READ_LIMIT) {
    if (!fs.existsSync(filePath)) return [];
    const lines = fs.readFileSync(filePath, 'utf8').split('\n').filter(Boolean);
    return lines.slice(-limit).reverse().map((line) => JSON.parse(line));
  }

  return { record, readRecent };
}

module.exports = { createAuditLog };
