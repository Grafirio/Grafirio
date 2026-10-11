import test from 'node:test';
import assert from 'node:assert/strict';
import normalizeHostAndPort from '../src/utils/connections/normalizeHostAndPort.js';
import DesktopConnectionError from '../src/services/connections/DesktopConnectionError.js';

test('normalizes explicit TCP endpoints without changing network location', () => {
  for (const host of ['localhost', '127.0.0.1', '192.168.1.7', '10.2.1.3', 'sql.example', '::1']) {
    assert.deepEqual(normalizeHostAndPort(host, 1433), { host, port: 1433 });
  }
  assert.deepEqual(normalizeHostAndPort(' sql.example:1444 ', ''), { host: 'sql.example', port: 1444 });
  assert.deepEqual(normalizeHostAndPort('[::1]:1444', 1444), { host: '::1', port: 1444 });
  assert.deepEqual(normalizeHostAndPort('[::ffff:192.168.1.7]:1433', 1433), { host: '::ffff:192.168.1.7', port: 1433 });
  assert.deepEqual(normalizeHostAndPort('localhost,1433', 1433), { host: 'localhost', port: 1433 });
});

test('rejects conflicting, invalid and connection-string-like endpoints', () => {
  assert.throws(() => normalizeHostAndPort('localhost:1444', 1433), /farklı/);
  for (const port of [-1, 0, 65536, 1.5, 'invalid']) assert.throws(() => normalizeHostAndPort('localhost', port));
  for (const host of ['', 'https://sql.example', 'host;Password=secret', 'host\\SQLEXPRESS', '[::1]:bad', '[1234]:1433', '[1234]', '1::2::3']) {
    assert.throws(() => normalizeHostAndPort(host, 1433));
  }
});

test('permanent native failure does not tell the user to repeatedly retry', () => {
  const error = new DesktopConnectionError('BRIDGE_IDENTITY_UNAVAILABLE', 'Sunucu yapılandırması gerekiyor (HTTP 502).', false);
  assert.equal(error.retryable, false);
  assert.match(error.message, /HTTP 502/);
  assert.doesNotMatch(error.message, /aynı düğmeyle/);
});
