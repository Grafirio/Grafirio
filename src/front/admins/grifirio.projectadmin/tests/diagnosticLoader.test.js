import { test } from 'node:test';
import assert from 'node:assert/strict';
import { createDiagnosticLoader } from '../src/utils/diagnosticLoader.js';

function setup() {
  const pending = []; const published = []; const errors = []; const loading = [];
  const loader = createDiagnosticLoader({
    request: (filters) => new Promise((resolve, reject) => pending.push({ filters, resolve, reject })),
    onData: (response) => published.push(response),
    onError: (error) => errors.push(error),
    onLoading: (value) => loading.push(value),
  });
  return { loader, pending, published, errors, loading };
}

test('requests one combined response and publishes its list/summary together', async () => {
  const state = setup();
  const filters = { days: 7, companyId: 'company', connectionId: 'connection', status: 'failed', errorCode: 'empty_result', search: 'Ciro', page: 2 };
  const task = state.loader.load(filters);
  assert.deepEqual(state.pending[0].filters, { ...filters, includeSummary: true });
  const response = { items: [{ id: 'request' }], total: 51, summary: { total: 51 } };
  state.pending[0].resolve(response); await task;
  assert.deepEqual(state.published, [response]);
  assert.deepEqual(state.loading, [true, false]);
});

test('older response cannot replace a newer list/summary pair', async () => {
  const state = setup();
  const old = state.loader.load({ companyId: 'old' });
  const current = state.loader.load({ companyId: 'new' });
  state.pending[1].resolve({ items: ['new'], summary: { total: 1 } }); await current;
  state.pending[0].resolve({ items: ['old'], summary: { total: 99 } }); await old;
  assert.deepEqual(state.published, [{ items: ['new'], summary: { total: 1 } }]);
  assert.deepEqual(state.loading, [true, true, false]);
});

test('filter cleanup blocks old responses during debounce and after unmount', async () => {
  const state = setup();
  const task = state.loader.load({});
  state.loader.invalidate();
  state.pending[0].resolve({ items: ['stale'], summary: {} }); await task;
  assert.deepEqual(state.published, []);
  assert.deepEqual(state.loading, [true]);
});

test('obsolete errors cannot replace the current request state', async () => {
  const state = setup();
  const old = state.loader.load({});
  state.loader.invalidate();
  const current = state.loader.load({});
  state.pending[0].reject(new Error('obsolete')); await old;
  assert.deepEqual(state.errors, [null, null]);
  state.pending[1].reject(new Error('current')); await current;
  assert.equal(state.errors.at(-1).message, 'current');
  assert.deepEqual(state.loading, [true, true, false]);
  assert.deepEqual(state.published, []);
});
