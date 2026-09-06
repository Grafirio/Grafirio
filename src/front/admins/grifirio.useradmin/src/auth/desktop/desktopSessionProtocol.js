export const DESKTOP_MESSAGE = Object.freeze({
  READY: 'ready',
  REFRESH: 'refresh',
  TOKENS: 'tokens',
  SIGN_IN: 'signIn',
  SIGN_OUT: 'signOut',
  SESSION_EXPIRED: 'sessionExpired',
});

export const DESKTOP_ERROR = Object.freeze({
  OFFLINE: 'offline',
  SIGNED_OUT: 'signedOut',
  TIMEOUT: 'timeout',
  INVALID_RESPONSE: 'invalidResponse',
});

export const REQUEST_TIMEOUT_MS = 30_000;

export class DesktopSessionError extends Error {
  constructor(code) {
    super(`Desktop session request failed: ${code}`);
    this.name = 'DesktopSessionError';
    this.code = code;
  }
}