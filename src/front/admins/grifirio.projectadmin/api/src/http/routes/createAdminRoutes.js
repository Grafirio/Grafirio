const express = require('express');
const { asyncHandler } = require('../asyncHandler');

const HTTP_ACCEPTED = 202;
const MAX_AUDIT_LIMIT = 500;

/** Authenticated (PLATFORM_ADMIN) operator API. */
function createAdminRoutes({ monitor, operations, settings, audit, config }) {
  const router = express.Router();

  router.get('/status', (_req, res) => {
    res.json({
      ...monitor.snapshot(),
      operation: operations.getOperationState(),
      autoHeal: settings.getAutoHeal(),
      emailAlertsEnabled: config.smtp.enabled,
    });
  });

  router.post('/services/:serviceId/:action', asyncHandler(async (req, res) => {
    await operations.controlService(req.params.serviceId, req.params.action, req.actor);
    res.status(HTTP_ACCEPTED).json({ ok: true });
  }));

  router.post('/docker/start', (req, res) => {
    res.status(HTTP_ACCEPTED).json(operations.startDocker(req.actor));
  });

  router.post('/infrastructure/up', (req, res) => {
    res.status(HTTP_ACCEPTED).json(operations.bringInfrastructureUp(req.actor));
  });

  router.get('/settings/autoheal', (_req, res) => res.json(settings.getAutoHeal()));

  router.put('/settings/autoheal', (req, res) => {
    const updated = settings.updateAutoHeal(req.body ?? {});
    audit.record({ actor: req.actor, action: 'autoheal-settings', target: 'settings', outcome: 'succeeded', detail: JSON.stringify(updated) });
    res.json(updated);
  });

  router.get('/audit', (req, res) => {
    const requested = Number.parseInt(req.query.limit, 10);
    const limit = Number.isInteger(requested) && requested > 0 ? Math.min(requested, MAX_AUDIT_LIMIT) : undefined;
    res.json(audit.readRecent(limit));
  });

  return router;
}

module.exports = { createAdminRoutes };
