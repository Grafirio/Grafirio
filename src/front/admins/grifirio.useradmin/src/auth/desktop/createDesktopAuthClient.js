import DesktopSessionBridge from './DesktopSessionBridge.js';
import parseDesktopToken from './parseDesktopToken.js';
import { DESKTOP_ERROR, DESKTOP_MESSAGE, DesktopSessionError } from './desktopSessionProtocol.js';

const MILLISECONDS_PER_SECOND = 1_000;
const DEFAULT_MIN_VALIDITY = 5;
const FORCE_REFRESH = -1;

export default function createDesktopAuthClient({ client = {}, webview, redirectUri, clientId }) {
  let initialization;
  let refreshRequest;
  let sessionVersion = 0;

  function clearSession() {
    const wasAuthenticated = client.authenticated;
    const hadInitError = Boolean(client.initError);
    sessionVersion++;
    for (const property of [
      'token', 'tokenParsed', 'idToken', 'idTokenParsed', 'refreshToken', 'refreshTokenParsed',
      'subject', 'realmAccess', 'resourceAccess', 'sessionId',
    ]) {
      delete client[property];
    }
    client.authenticated = false;
    client.initError = undefined;
    if (wasAuthenticated || hadInitError) client.onAuthLogout?.();
  }

  async function requestTokens(type) {
    const requestedVersion = sessionVersion;
    const response = await bridge.request(type);
    // A reply queued before a native sign-out must not resurrect the old session.
    if (requestedVersion !== sessionVersion) {
      throw new DesktopSessionError(DESKTOP_ERROR.SIGNED_OUT);
    }
    applyResponse(response);
  }

  function applyResponse(response) {
    if (response.error) {
      const code = Object.values(DESKTOP_ERROR).includes(response.error)
        ? response.error : DESKTOP_ERROR.INVALID_RESPONSE;
      throw new DesktopSessionError(code);
    }

    const { token, idToken } = response.tokens ?? {};
    const tokenParsed = parseDesktopToken(token);
    const idTokenParsed = idToken === undefined ? undefined : parseDesktopToken(idToken);
    if (idTokenParsed && idTokenParsed.sub !== tokenParsed.sub) {
      throw new DesktopSessionError(DESKTOP_ERROR.INVALID_RESPONSE);
    }

    const wasAuthenticated = client.authenticated;
    // Never copy refresh credentials, including those accidentally sent by an older host.
    Object.assign(client, {
      token, tokenParsed, idToken, idTokenParsed,
      subject: tokenParsed.sub,
      realmAccess: tokenParsed.realm_access,
      resourceAccess: tokenParsed.resource_access,
      sessionId: tokenParsed.sid,
      authenticated: true,
      initError: undefined,
    });
    if (wasAuthenticated) client.onAuthRefreshSuccess?.();
    else client.onAuthSuccess?.();
  }

  function handleBroadcast(response) {
    try {
      applyResponse(response);
    } catch (error) {
      if (error.code === DESKTOP_ERROR.SIGNED_OUT) clearSession();
      else client.onAuthRefreshError?.(error);
    }
  }

  const bridge = new DesktopSessionBridge(webview, handleBroadcast, clearSession);

  async function initialize() {
    client.didInitialize = true;
    try {
      bridge.connect();
      await requestTokens(DESKTOP_MESSAGE.READY);
    } catch (error) {
      if (error.code === DESKTOP_ERROR.SIGNED_OUT) {
        clearSession();
      } else {
        client.initError = error;
        // The provider otherwise remains on its loading screen after an init failure.
        client.onReady?.(client.authenticated);
        throw error;
      }
    }
    client.onReady?.(client.authenticated);
    return client.authenticated;
  }

  async function refresh() {
    try {
      await requestTokens(DESKTOP_MESSAGE.REFRESH);
      return true;
    } catch (error) {
      if (error.code === DESKTOP_ERROR.SIGNED_OUT) {
        const wasAuthenticated = client.authenticated;
        clearSession();
        // Do not echo native broadcasts; acknowledge only an explicit unauthorized reply.
        if (wasAuthenticated) bridge.send(DESKTOP_MESSAGE.SESSION_EXPIRED);
      }
      client.onAuthRefreshError?.(error);
      throw error;
    } finally {
      refreshRequest = undefined;
    }
  }

  function signIn() {
    bridge.send(DESKTOP_MESSAGE.SIGN_IN);
    return Promise.resolve();
  }

  function localUrl() {
    // URL-only consumers must never navigate the WebView to an identity provider.
    return redirectUri;
  }

  // Adapt the existing singleton so direct service imports share the provider's session.
  Object.assign(client, {
    isDesktop: true,
    clientId,
    authenticated: false,
    didInitialize: false,
    timeSkew: 0,
    init() {
      initialization ??= initialize();
      return initialization;
    },
    isTokenExpired(minValidity = 0) {
      if (!client.tokenParsed) return true;
      return client.tokenParsed.exp - Date.now() / MILLISECONDS_PER_SECOND <= minValidity;
    },
    updateToken(minValidity = DEFAULT_MIN_VALIDITY) {
      if (refreshRequest) return refreshRequest;
      if (minValidity !== FORCE_REFRESH && !client.isTokenExpired(minValidity)) {
        return Promise.resolve(false);
      }
      refreshRequest = refresh();
      return refreshRequest;
    },
    hasRealmRole(role) {
      return client.realmAccess?.roles?.includes(role) ?? false;
    },
    hasResourceRole(role, resource = client.clientId) {
      return client.resourceAccess?.[resource]?.roles?.includes(role) ?? false;
    },
    login: signIn,
    register: signIn,
    accountManagement: signIn,
    logout() {
      bridge.send(DESKTOP_MESSAGE.SIGN_OUT);
      return Promise.resolve();
    },
    createLoginUrl: async () => localUrl(),
    createRegisterUrl: async () => localUrl(),
    createLogoutUrl: localUrl,
    createAccountUrl: localUrl,
    clearToken: clearSession,
    async loadUserInfo() {
      return client.tokenParsed;
    },
    async loadUserProfile() {
      const claims = client.tokenParsed ?? {};
      return {
        id: claims.sub, username: claims.preferred_username, email: claims.email,
        firstName: claims.given_name, lastName: claims.family_name,
      };
    },
  });
  clearSession();
  return client;
}