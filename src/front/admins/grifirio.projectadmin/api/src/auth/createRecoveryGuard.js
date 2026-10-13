const crypto = require('node:crypto');

const LOOPBACK_ADDRESSES = new Set(['127.0.0.1', '::1', '::ffff:127.0.0.1']);
const MAX_FAILED_ATTEMPTS = 5;
const LOCKOUT_MS = 60_000;

/**
 * Break-glass access for when Docker or Keycloak is down and tokens cannot be validated:
 * only from the local machine, only with the shared recovery key, with a lockout on repeated failures.
 */
function createRecoveryGuard({ recoveryKey, now = Date.now }) {
  let failedAttempts = 0;
  let lockedUntil = 0;

  return (req, res, next) => {
    if (!recoveryKey) return res.status(404).json({ error: 'Recovery access is not configured' });
    if (!LOOPBACK_ADDRESSES.has(req.socket.remoteAddress)) return res.status(403).json({ error: 'Local access only' });
    if (now() < lockedUntil) return res.status(429).json({ error: 'Too many attempts; try again later' });

    const supplied = Buffer.from(req.get('x-recovery-key') ?? '');
    const expected = Buffer.from(recoveryKey);
    const isValid = supplied.length === expected.length && crypto.timingSafeEqual(supplied, expected);

    if (!isValid) {
      failedAttempts += 1;
      if (failedAttempts >= MAX_FAILED_ATTEMPTS) {
        failedAttempts = 0;
        lockedUntil = now() + LOCKOUT_MS;
      }
      return res.status(401).json({ error: 'Invalid recovery key' });
    }

    failedAttempts = 0;
    req.actor = { type: 'recovery', id: 'recovery-key' };
    return next();
  };
}

module.exports = { createRecoveryGuard };
