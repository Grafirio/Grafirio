import { DESKTOP_ERROR, DesktopSessionError } from './desktopSessionProtocol.js';

const JWT_SEGMENT_COUNT = 3;
const BASE64_BLOCK_SIZE = 4;

export default function parseDesktopToken(token) {
  try {
    if (typeof token !== 'string') throw new Error();
    const segments = token.split('.');
    if (segments.length !== JWT_SEGMENT_COUNT) throw new Error();
    const payload = segments[1].replace(/-/g, '+').replace(/_/g, '/');
    const paddedPayload = payload.padEnd(
      Math.ceil(payload.length / BASE64_BLOCK_SIZE) * BASE64_BLOCK_SIZE, '=',
    );
    const bytes = Uint8Array.from(atob(paddedPayload), (character) => character.charCodeAt(0));
    const claims = JSON.parse(new TextDecoder().decode(bytes));
    if (!claims || typeof claims !== 'object' || Array.isArray(claims)
      || !Number.isFinite(claims.exp) || typeof claims.sub !== 'string' || !claims.sub) {
      throw new Error();
    }
    // Native validates signatures, issuer and audience before delivering these claims.
    return claims;
  } catch {
    throw new DesktopSessionError(DESKTOP_ERROR.INVALID_RESPONSE);
  }
}