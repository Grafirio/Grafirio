import test from 'node:test';
import assert from 'node:assert/strict';
import { Buffer } from 'node:buffer';
import { readFile } from 'node:fs/promises';
import { createRequire } from 'node:module';
import createDesktopAuthClient from './createDesktopAuthClient.js';
import { REQUEST_TIMEOUT_MS } from './desktopSessionProtocol.js';

const require = createRequire(import.meta.url);
const { ReactKeycloakProvider } = require('@react-keycloak/web');
const FUTURE_EXPIRATION = 4_000_000_000;

function token(claims = {}) {
  return `header.${Buffer.from(JSON.stringify({
    sub: 'user', exp: FUTURE_EXPIRATION, ...claims,
  })).toString('base64url')}.signature`;
}

function fixture() {
  const listeners = new Set();
  const sent = [];
  const webview = {
    addEventListener(type, handler) {
      assert.equal(type, 'message');
      listeners.add(handler);
    },
    removeEventListener(type, handler) { listeners.delete(handler); },
    postMessage(message) { sent.push(message); },
  };
  const originalClient = { login() { assert.fail('Browser adapter must not run'); } };
  const client = createDesktopAuthClient({
    client: originalClient, webview, redirectUri: 'https://panel.example/settings', clientId: 'panel',
  });
  const emit = (data) => listeners.forEach((listener) => listener({ data }));
  const reply = (response = {}, request = sent.at(-1)) => emit({
    type: 'tokens', requestId: request.requestId,
    tokens: { token: token(), idToken: token() }, ...response,
  });
  return { client, originalClient, webview, sent, emit, reply, listeners };
}

async function authenticatedFixture() {
  const context = fixture();
  const initialized = context.client.init();
  context.reply();
  assert.equal(await initialized, true);
  return context;
}

test('init is idempotent, sends ready only on mount, and adapts the original singleton', async () => {
  const { client, originalClient, sent, reply, listeners } = fixture();
  assert.equal(client, originalClient);
  assert.equal(sent.length, 0);
  const initialization = client.init({ onLoad: 'login-required', token: 'ignored' });
  assert.equal(client.init(), initialization);
  assert.equal(listeners.size, 1);
  assert.equal(sent[0].type, 'ready');
  assert.equal(typeof sent[0].requestId, 'string');
  assert.equal(client.authenticated, false);
  reply();
  assert.equal(await initialization, true);
  assert.equal(sent.length, 1);
});

test('signed-out ready initializes provider without invoking sign-in or reporting expiry', async () => {
  const { client, sent, reply } = fixture();
  const ready = [];
  client.onReady = (value) => ready.push(value);
  const initialization = client.init();
  reply({ error: 'signedOut' });
  assert.equal(await initialization, false);
  assert.deepEqual(ready, [false]);
  assert.equal(client.initError, undefined);
  assert.deepEqual(sent.map(({ type }) => type), ['ready']);
});

test('claims support Unicode, roles and optional identity tokens, never refresh credentials', async () => {
  const { client, reply } = fixture();
  const initialization = client.init();
  reply({ tokens: {
    token: token({ name: 'Çağrı', realm_access: { roles: ['admin'] }, resource_access: {
      panel: { roles: ['read'] }, other: { roles: ['write'] },
    } }), refreshToken: 'must-not-be-stored',
  } });
  await initialization;
  assert.equal(client.subject, 'user');
  assert.equal(client.tokenParsed.name, 'Çağrı');
  assert.equal(client.hasRealmRole('admin'), true);
  assert.equal(client.hasRealmRole('missing'), false);
  assert.equal(client.hasResourceRole('read'), true);
  assert.equal(client.hasResourceRole('write', 'other'), true);
  assert.equal(client.hasResourceRole('write'), false);
  assert.equal(client.idToken, undefined);
  assert.equal('refreshToken' in client, false);
  assert.equal('refreshTokenParsed' in client, false);
});

test('refresh is native-only, correlated and coalesced; valid tokens skip unnecessary work', async () => {
  const { client, sent, reply, emit } = await authenticatedFixture();
  assert.equal(await client.updateToken(), false);
  const refreshing = client.updateToken(-1);
  assert.equal(client.updateToken(-1), refreshing);
  assert.equal(sent.at(-1).type, 'refresh');
  assert.equal(sent.length, 2);
  assert.notEqual(sent[0].requestId, sent[1].requestId);
  emit({ type: 'tokens', requestId: 'unknown', tokens: { token: token({ sub: 'wrong' }) } });
  assert.equal(client.subject, 'user');
  reply({ tokens: { token: token({ company_id: 'company' }), idToken: token(), refreshToken: 'ignored' } });
  assert.equal(await refreshing, true);
  assert.equal(client.tokenParsed.company_id, 'company');
  assert.equal('refreshToken' in client, false);
});

test('expired access token requests native refresh without locally revoking the session', async () => {
  const { client, emit, sent, reply } = await authenticatedFixture();
  emit({ type: 'tokens', tokens: { token: token({ exp: 1 }) } });
  assert.equal(client.isTokenExpired(), true);
  assert.equal(client.authenticated, true);
  const refreshing = client.updateToken();
  assert.equal(sent.at(-1).type, 'refresh');
  reply();
  await refreshing;
  assert.equal(client.isTokenExpired(), false);
});

test('offline refresh preserves credentials and rejects without sessionExpired', async () => {
  const { client, reply, sent } = await authenticatedFixture();
  const previousToken = client.token;
  const refreshing = client.updateToken(-1);
  reply({ error: 'offline' });
  await assert.rejects(refreshing, { code: 'offline' });
  assert.equal(client.token, previousToken);
  assert.equal(client.authenticated, true);
  assert.deepEqual(sent.map(({ type }) => type), ['ready', 'refresh']);
});

test('explicit unauthorized refresh clears session and acknowledges expiry once', async () => {
  const { client, reply, sent, emit } = await authenticatedFixture();
  let logoutCount = 0;
  client.onAuthLogout = () => logoutCount++;
  const refreshing = client.updateToken(-1);
  reply({ error: 'signedOut' });
  await assert.rejects(refreshing, { code: 'signedOut' });
  emit({ type: 'sessionExpired' });
  assert.equal(client.authenticated, false);
  assert.equal(client.token, undefined);
  assert.equal(client.idTokenParsed, undefined);
  assert.equal(logoutCount, 1);
  assert.deepEqual(sent.map(({ type }) => type), ['ready', 'refresh', 'sessionExpired']);
});

test('native expiry cancels pending refresh and ignores late replies without echo', async () => {
  const { client, emit, reply, sent } = await authenticatedFixture();
  const refreshing = client.updateToken(-1);
  const request = sent.at(-1);
  emit({ type: 'sessionExpired' });
  reply({}, request);
  await assert.rejects(refreshing, { code: 'signedOut' });
  assert.equal(client.authenticated, false);
  assert.deepEqual(sent.map(({ type }) => type), ['ready', 'refresh']);
});

test('native expiry during ready resolves initialization as unauthenticated', async () => {
  const { client, emit } = fixture();
  const initialization = client.init();
  emit({ type: 'sessionExpired' });
  assert.equal(await initialization, false);
});

test('native sign-out wins even when a refresh reply has already been queued', async () => {
  const { client, reply, emit, sent } = await authenticatedFixture();
  const refreshing = client.updateToken(-1);
  reply();
  emit({ type: 'sessionExpired' });
  await assert.rejects(refreshing, { code: 'signedOut' });
  assert.equal(client.authenticated, false);
  assert.equal(client.token, undefined);
  assert.deepEqual(sent.map(({ type }) => type), ['ready', 'refresh']);
});

test('signed-out broadcast recovers the initial error screen without starting login', async () => {
  const { client, reply, emit, sent } = fixture();
  const initialization = client.init();
  reply({ error: 'offline' });
  await assert.rejects(initialization, { code: 'offline' });
  let logoutCount = 0;
  client.onAuthLogout = () => logoutCount++;
  emit({ type: 'tokens', error: 'signedOut' });
  assert.equal(client.initError, undefined);
  assert.equal(client.authenticated, false);
  assert.equal(logoutCount, 1);
  assert.deepEqual(sent.map(({ type }) => type), ['ready']);
});

test('request timeouts are 30 seconds, preserve session and discard late responses', async (context) => {
  context.mock.timers.enable({ apis: ['setTimeout'] });
  const { client, reply, sent } = await authenticatedFixture();
  const previousToken = client.token;
  const refreshing = client.updateToken(-1);
  const rejected = assert.rejects(refreshing, { code: 'timeout' });
  context.mock.timers.tick(REQUEST_TIMEOUT_MS);
  await rejected;
  reply({ tokens: { token: token({ sub: 'late' }) } });
  assert.equal(client.token, previousToken);
  assert.equal(client.authenticated, true);
  assert.equal(sent.length, 2);
});

test('initial offline and timeout finish loading without revocation; token broadcast recovers', async (context) => {
  context.mock.timers.enable({ apis: ['setTimeout'] });
  for (const code of ['offline', 'timeout']) {
    const { client, reply, emit, sent } = fixture();
    const ready = [];
    client.onReady = (value) => ready.push(value);
    const initialization = client.init();
    const rejected = assert.rejects(initialization, { code });
    if (code === 'offline') reply({ error: code });
    else context.mock.timers.tick(REQUEST_TIMEOUT_MS);
    await rejected;
    assert.deepEqual(ready, [false]);
    assert.equal(client.initError.code, code);
    assert.equal(sent.length, 1);
    emit({ type: 'tokens', tokens: { token: token(), idToken: token() } });
    assert.equal(client.authenticated, true);
    assert.equal(client.initError, undefined);
  }
});

test('missing native bridge fails closed without browser fallback', async () => {
  const client = createDesktopAuthClient({ redirectUri: 'https://panel.example/' });
  await assert.rejects(client.init(), { code: 'offline' });
  assert.equal(client.authenticated, false);
});

test('malformed messages cannot change session; malformed tokens reject without revocation', async () => {
  const { client, emit, reply } = await authenticatedFixture();
  const previousToken = client.token;
  for (const data of [null, [], 'sessionExpired', '{"type":"sessionExpired"}', { type: 'other' }]) {
    emit(data);
  }
  const errors = [];
  client.onAuthRefreshError = (error) => errors.push(error.code);
  emit({ type: 'tokens', tokens: { token: 'invalid' } });
  assert.deepEqual(errors, ['invalidResponse']);
  const refreshing = client.updateToken(-1);
  reply({ tokens: { token: token(), idToken: token({ sub: 'different' }) } });
  await assert.rejects(refreshing, { code: 'invalidResponse' });
  assert.equal(client.token, previousToken);
  assert.equal(client.authenticated, true);
});

test('login, register and account route to signIn; logout waits for native confirmation', async () => {
  const { client, sent, emit } = await authenticatedFixture();
  await client.login();
  await client.register();
  await client.accountManagement();
  await client.logout();
  assert.deepEqual(sent.slice(1), [
    { type: 'signIn' }, { type: 'signIn' }, { type: 'signIn' }, { type: 'signOut' },
  ]);
  assert.equal(client.authenticated, true);
  emit({ type: 'sessionExpired' });
  assert.equal(client.authenticated, false);
  for (const method of ['createLoginUrl', 'createRegisterUrl', 'createLogoutUrl', 'createAccountUrl']) {
    assert.equal(await client[method](), 'https://panel.example/settings');
  }
});

test('broadcast rotation updates provider token callbacks including access-token-only sessions', async () => {
  const { client, reply, emit } = fixture();
  const snapshots = [];
  const provider = new ReactKeycloakProvider({
    authClient: client, autoRefreshToken: false,
    onTokens: (tokens) => snapshots.push(tokens),
  });
  provider.setState = (state) => { provider.state = { ...provider.state, ...state }; };
  provider.componentDidMount();
  reply();
  await client.init();
  assert.equal(provider.state.initialized, true);
  const rotatedToken = token({ company_id: 'new-company' });
  emit({ type: 'tokens', tokens: { token: rotatedToken } });
  assert.equal(snapshots.at(-1).token, rotatedToken);
  assert.equal(snapshots.at(-1).refreshToken, undefined);
  assert.equal(client.authenticated, true);
  emit({ type: 'sessionExpired' });
  assert.equal(snapshots.at(-1).token, undefined);
});

test('entry-point preserves browser configuration and never consumes injected credentials', async () => {
  const entry = await readFile(new URL('../../main.jsx', import.meta.url), 'utf8');
  const selection = await readFile(new URL('./authClient.js', import.meta.url), 'utf8');
  const provider = await readFile(new URL('./DesktopKeycloakProvider.jsx', import.meta.url), 'utf8');
  assert.match(entry, /onLoad: 'login-required'/);
  assert.doesNotMatch(entry, /__GRAFIRIO_DESKTOP__\?\.tokens/);
  assert.match(selection, /window\.top === window\.self/);
  assert.match(selection, /: keycloak/);
  assert.match(provider, /autoRefreshToken=\{false\}/);
  assert.match(provider, /onTokens=\{handleTokens\}/);
});