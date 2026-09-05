import test, { mock } from 'node:test';
import assert from 'node:assert/strict';

const submit = mock.fn();
const status = mock.fn();
const result = mock.fn();
mock.module('../src/services/dataAnalysisService.js', { exports: {
  submitAgentQuery: submit, getAgentQueryStatus: status, getAgentQueryResult: result,
} });
const { default: askViaAgent } = await import('../src/services/askViaAgent.js');
const match = { fromTable: 'Orders', fromColumn: 'CustomerId', toTable: 'Customers', toColumn: 'Id' };

test('polling stops at clarification after processing, without fetching a completed result', async context => {
  context.mock.timers.enable({ apis: ['setTimeout'] });
  submit.mock.mockImplementation(async () => ({ success: true, queryId: 'query' }));
  let polls = 0;
  status.mock.mockImplementation(async () => ++polls === 1
    ? { status: 'processing' }
    : { status: 'clarification', error: 'Confirm relationship', pendingConfirmations: [match] });
  const progress = [];
  const responsePromise = askViaAgent('Original', { connectionId: 'connection', onProgress: message => progress.push(message) });
  await Promise.resolve();
  context.mock.timers.tick(3000);
  await Promise.resolve();
  await Promise.resolve();
  context.mock.timers.tick(3000);
  const response = await responsePromise;
  assert.equal(response.success, false);
  assert.equal(response.needsClarification, true);
  assert.equal(response.queryId, 'query');
  assert.deepEqual(response.pendingConfirmations, [match]);
  assert.equal(polls, 2);
  assert.equal(result.mock.callCount(), 0);
  assert.equal(progress.length, 2);
});

test('immediate clarification returns without polling and preserves parent id on submission', async () => {
  submit.mock.mockImplementation(async () => ({
    status: 'clarification', queryId: 'child', clarificationQuestion: 'Which period?',
  }));
  const count = status.mock.callCount();
  const response = await askViaAgent('Original', { connectionId: 'connection', parentQueryId: 'parent' });
  assert.equal(response.needsClarification, true);
  assert.equal(response.queryId, 'child');
  assert.equal(status.mock.callCount(), count);
  assert.deepEqual(submit.mock.calls.at(-1).arguments, ['connection', 'Original', 'parent']);
});

test('completed result containing Python clarification is not reported as success', async context => {
  context.mock.timers.enable({ apis: ['setTimeout'] });
  submit.mock.mockImplementation(async () => ({ success: true, queryId: 'completed-query' }));
  status.mock.mockImplementation(async () => ({ status: 'completed' }));
  result.mock.mockImplementation(async () => ({ result: {
    needsClarification: true, clarificationQuestion: 'Which target?', pendingConfirmations: [match],
  } }));
  const responsePromise = askViaAgent('Original', { connectionId: 'connection' });
  await Promise.resolve();
  context.mock.timers.tick(3000);
  const response = await responsePromise;
  assert.equal(response.success, false);
  assert.equal(response.needsClarification, true);
  assert.equal(response.queryId, 'completed-query');
  assert.deepEqual(response.pendingConfirmations, [match]);
});

test('polling failures preserve the submitted query identity for history and retry', async context => {
  context.mock.timers.enable({ apis: ['setTimeout'] });
  submit.mock.mockImplementation(async () => ({ success: true, queryId: 'failed-query' }));
  status.mock.mockImplementation(async () => { throw new Error('Network Error'); });
  const responsePromise = askViaAgent('Original', { connectionId: 'connection' });
  await Promise.resolve();
  context.mock.timers.tick(3000);
  const response = await responsePromise;
  assert.equal(response.success, false);
  assert.equal(response.queryId, 'failed-query');
  assert.equal(response.error, 'Network Error');
});