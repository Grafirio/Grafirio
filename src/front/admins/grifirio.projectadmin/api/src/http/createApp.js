const express = require('express');
const cors = require('cors');
const { OpsError } = require('../OpsError');
const { requirePlatformAdmin } = require('../auth/requirePlatformAdmin');
const { createAdminRoutes } = require('./routes/createAdminRoutes');
const { createRecoveryRoutes } = require('./routes/createRecoveryRoutes');

const HTTP_INTERNAL_ERROR = 500;
const JSON_BODY_LIMIT = '10kb';

function createApp({ config, monitor, operations, settings, audit, verifyToken, recoveryGuard, logger = console }) {
  const app = express();
  app.disable('x-powered-by');
  app.use(cors({ origin: config.allowedOrigins, allowedHeaders: ['Authorization', 'Content-Type'] }));
  app.use(express.json({ limit: JSON_BODY_LIMIT }));

  app.get('/health', (_req, res) => res.json({ status: 'ok' }));

  // Detail-free status for the end-user outage banner; deliberately unauthenticated.
  app.get('/api/ops/status/summary', (_req, res) => res.json(monitor.publicSummary()));

  app.use('/api/ops', requirePlatformAdmin(verifyToken), createAdminRoutes({ monitor, operations, settings, audit, config }));
  app.use('/recovery', createRecoveryRoutes({ monitor, operations, recoveryGuard }));

  // eslint-disable-next-line no-unused-vars
  app.use((error, _req, res, _next) => {
    if (error instanceof OpsError) return res.status(error.status).json({ error: error.message });
    logger.error('Unhandled request error', error);
    return res.status(HTTP_INTERNAL_ERROR).json({ error: 'Internal error' });
  });

  return app;
}

module.exports = { createApp };
