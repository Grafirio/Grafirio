import keycloak from '../../keycloak.js';
import createDesktopAuthClient from './createDesktopAuthClient.js';

const isDesktop = Boolean(window.__GRAFIRIO_DESKTOP__) && window.top === window.self;

// The marker is injected before module evaluation; credentials arrive only after ready.
const authClient = isDesktop
  ? createDesktopAuthClient({
    client: keycloak,
    webview: window.chrome?.webview,
    redirectUri: window.location.href,
    clientId: import.meta.env.VITE_KEYCLOAK_CLIENT_ID || 'grafirio-client',
  })
  : keycloak;

export default authClient;