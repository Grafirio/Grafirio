const express = require('express');
const path = require('node:path');

const HTTP_ACCEPTED = 202;
const PUBLIC_DIR = path.join(__dirname, '..', '..', '..', 'public');
const RECOVERY_CSP = "default-src 'self'; style-src 'self' 'unsafe-inline'";

/** Break-glass routes for when Docker/Keycloak are down: loopback + recovery key, and only two actions. */
function createRecoveryRoutes({ monitor, operations, recoveryGuard }) {
  const router = express.Router();

  router.use((_req, res, next) => {
    res.set('Content-Security-Policy', RECOVERY_CSP);
    next();
  });

  // The page itself holds no secrets; every action below is guarded.
  router.get('/', (_req, res) => res.sendFile(path.join(PUBLIC_DIR, 'recovery.html')));
  router.get('/recovery.js', (_req, res) => res.sendFile(path.join(PUBLIC_DIR, 'recovery.js')));

  router.get('/status', recoveryGuard, (_req, res) => res.json({
    ...monitor.snapshot(),
    operation: operations.getOperationState(),
  }));

  router.post('/docker/start', recoveryGuard, (req, res) => {
    res.status(HTTP_ACCEPTED).json(operations.startDocker(req.actor));
  });

  router.post('/infrastructure/up', recoveryGuard, (req, res) => {
    res.status(HTTP_ACCEPTED).json(operations.bringInfrastructureUp(req.actor));
  });

  return router;
}

module.exports = { createRecoveryRoutes };
