const { createRemoteJWKSet, jwtVerify } = require('jose');

/**
 * Verifies Keycloak access tokens (signature, issuer, expiry) and extracts the platform-admin flag.
 * jose caches the signing keys, so already-seen keys keep validating if Keycloak briefly goes down.
 */
function createTokenVerifier({ oidcIssuer, rolesClaim, platformAdminRole }, keySet) {
  const jwks = keySet ?? createRemoteJWKSet(new URL(`${oidcIssuer}/protocol/openid-connect/certs`));

  return async function verifyToken(token) {
    const { payload } = await jwtVerify(token, jwks, { issuer: oidcIssuer });
    const roles = [].concat(payload[rolesClaim] ?? []);
    return {
      id: payload.preferred_username ?? payload.sub,
      isPlatformAdmin: roles.includes(platformAdminRole),
    };
  };
}

module.exports = { createTokenVerifier };
