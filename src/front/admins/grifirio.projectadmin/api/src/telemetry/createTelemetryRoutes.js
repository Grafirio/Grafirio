const express = require('express');
const { timingSafeEqual } = require('node:crypto');
const { requirePlatformAdmin } = require('../auth/requirePlatformAdmin');
const { OpsError } = require('../OpsError');

function createTelemetryRoutes({ archive, producers, verifyToken, audit }) {
  const router = express.Router();
  const handler = (fn) => (req, res, next) => Promise.resolve().then(() => fn(req, res)).catch(next);
  router.post('/ingest', (req, res, next) => {
    const supplied = Buffer.from(req.get('X-Grafirio-Telemetry-Key') || '');
    const producer = producers.find((candidate) => {
      const expected = Buffer.from(candidate.key || '');
      return expected.length >= 32 && supplied.length === expected.length && timingSafeEqual(expected, supplied);
    });
    if (!producer) return res.status(401).json({ error: 'Telemetry producer authentication required' });
    req.telemetryProducer = producer; next();
  }, express.json({ limit: '256kb' }), handler(async (req, res) => res.json(await archive.ingest(req.body, req.telemetryProducer))));
  router.use(requirePlatformAdmin(verifyToken));
  router.get('/events', handler(async (req, res) => { res.set('Cache-Control', 'no-store'); res.json(await archive.query(req.query)); }));
  router.get('/status', handler(async (_req, res) => { res.set('Cache-Control', 'no-store'); res.json(await archive.status()); }));
  router.put('/settings', express.json({ limit: '10kb' }), handler(async (req, res) => {
    const before = await archive.settings();
    const proposed = { retentionDays: req.body?.retentionDays, maxRows: req.body?.maxRows, maxBytes: req.body?.maxBytes };
    if (Object.values(proposed).some((value) => !Number.isSafeInteger(value))) {
      await audit.record({ actor: req.actor, action: 'telemetry-retention', target: 'telemetry', outcome: 'failed' });
      throw new OpsError(400, 'Invalid retention policy');
    }
    // Audit intent before accepting a destructive retention change, then record its outcome.
    await audit.record({ actor: req.actor, action: 'telemetry-retention-requested', target: 'telemetry', outcome: 'requested', detail: JSON.stringify({ before, after: proposed }) });
    try {
      const after = await archive.updateSettings(proposed);
      await audit.record({ actor: req.actor, action: 'telemetry-retention', target: 'telemetry', outcome: 'succeeded', detail: JSON.stringify({ before, after }) });
      res.json(after);
    } catch (error) {
      await audit.record({ actor: req.actor, action: 'telemetry-retention', target: 'telemetry', outcome: 'failed' }); throw error;
    }
  }));
  return router;
}
module.exports = { createTelemetryRoutes };
