const BEARER_PREFIX = 'Bearer ';

/** Express middleware: valid Keycloak token with the platform-admin role, otherwise 401/403. */
function requirePlatformAdmin(verifyToken) {
  return async (req, res, next) => {
    const header = req.get('authorization') ?? '';
    if (!header.startsWith(BEARER_PREFIX)) return res.status(401).json({ error: 'Authentication required' });

    let identity;
    try {
      identity = await verifyToken(header.slice(BEARER_PREFIX.length));
    } catch {
      // Any verification failure (bad signature, expired, issuer mismatch, Keycloak unreachable) is a 401.
      return res.status(401).json({ error: 'Invalid or expired token' });
    }

    if (!identity.isPlatformAdmin) return res.status(403).json({ error: 'PLATFORM_ADMIN role required' });

    req.actor = { type: 'user', id: identity.id };
    return next();
  };
}

module.exports = { requirePlatformAdmin };
