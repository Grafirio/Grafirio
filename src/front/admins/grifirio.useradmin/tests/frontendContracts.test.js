import test from 'node:test';
import assert from 'node:assert/strict';
import buildConnectionUpdate from '../src/utils/connections/buildConnectionUpdate.js';
import reconcileAnalysisState from '../src/utils/analysis/reconcileAnalysisState.js';
import selectAnalysisAnswers from '../src/utils/analysis/selectAnalysisAnswers.js';
import resolveAgentResponse from '../src/utils/resolveAgentResponse.js';
import getQueryFailure from '../src/utils/getQueryFailure.js';
import restoreFromHistory from '../src/utils/restoreFromHistory.js';

const match = { fromTable: 'dbo.Orders', fromColumn: 'CustomerId', toTable: 'dbo.Customers', toColumn: 'Id' };
const previous = {
  status: 'awaiting_answers', answers: { q1: 'Evet', q2: 'Hayır', stale: 'Evet' },
  questions: [{ id: 'q1' }, { id: 'q2' }], minimized: true, open: false,
};

test('connection updates omit blank/null/omitted passwords and preserve explicit rotation exactly', () => {
  for (const password of ['', null, undefined]) {
    assert.deepEqual(buildConnectionUpdate({ name: 'Source', password }), { name: 'Source' });
  }
  assert.deepEqual(buildConnectionUpdate({ name: 'Source' }), { name: 'Source' });
  assert.equal(buildConnectionUpdate({ password: ' new secret ' }).password, ' new secret ');
  assert.equal(buildConnectionUpdate({ password: ' ' }).password, ' ');
});

test('partial answers are submitted without stale ids or requiring every question', () => {
  assert.deepEqual(selectAnalysisAnswers(previous.questions, { q1: 'Evet', stale: 'Hayır' }), { q1: 'Evet' });
  assert.deepEqual(selectAnalysisAnswers(previous.questions, { q1: '', q2: ' ' }), {});
});

test('partial answer response stays waiting and refreshes remaining questions', () => {
  for (const status of ['awaiting_answers', 'await_answers']) {
    const next = reconcileAnalysisState(previous, { status, questions: [{ id: 'q2' }, { id: 'q3' }] });
    assert.equal(next.status, 'awaiting_answers');
    assert.equal(next.running, false);
    assert.equal(next.open, true);
    assert.equal(next.minimized, false);
    assert.deepEqual(next.questions, [{ id: 'q2' }, { id: 'q3' }]);
    assert.deepEqual(next.answers, { q2: 'Hayır' });
  }
});

test('ready/analyzing/failed are driven by server status rather than answer count', () => {
  assert.equal(reconcileAnalysisState(previous, { status: 'analyzing' }).running, true);
  const ready = reconcileAnalysisState(previous, { status: 'ready' });
  assert.equal(ready.status, 'ready');
  assert.deepEqual(ready.answers, {});
  assert.equal(reconcileAnalysisState(previous, { status: 'failed', error: 'Failure' }).error, 'Failure');
});

test('processing followed by structured clarification keeps identity and proposals', () => {
  assert.equal(resolveAgentResponse({ status: 'processing' }, 'query'), null);
  const result = resolveAgentResponse({
    status: 'clarification', clarificationQuestion: 'İlişkiyi onaylıyor musunuz?', pendingConfirmations: [match],
  }, 'query');
  assert.equal(result.success, false);
  assert.equal(result.needsClarification, true);
  assert.equal(result.queryId, 'query');
  assert.deepEqual(result.pendingConfirmations, [match]);
  assert.equal(result.error, 'İlişkiyi onaylıyor musunuz?');
});

test('nested Python clarification and generic clarification never manufacture relationship proposals', () => {
  const nested = resolveAgentResponse({ status: 'completed', result: {
    needsClarification: true, clarificationQuestion: 'Hangi dönem?',
  } }, 'query');
  assert.equal(nested.success, false);
  assert.equal(nested.needsClarification, true);
  assert.deepEqual(nested.pendingConfirmations, []);
  assert.equal(nested.error, 'Hangi dönem?');
});

test('completed results retain charts and inferred confirmations; failed stays failure', () => {
  const result = resolveAgentResponse({ result: { charts: [{ type: 'bar' }], audit: { pendingConfirmations: [match] } } }, 'done');
  assert.equal(result.success, true);
  assert.equal(result.charts.length, 1);
  assert.deepEqual(result.pendingConfirmations, [match]);
  assert.equal(resolveAgentResponse({ status: 'failed', error: 'Failure' }, 'failed').error, 'Failure');
});

test('HTTP clarification status works without a separate needsClarification flag', () => {
  const result = getQueryFailure({ response: { data: {
    status: 'clarification', clarificationQuestion: 'Onay?', queryId: 'query', pendingConfirmations: [match],
  } } });
  assert.equal(result.needsClarification, true);
  assert.equal(result.error, 'Onay?');
});

test('history preserves top-level structured clarification and its original resume question', () => {
  const { nodes } = restoreFromHistory([{
    queryId: 'query', status: 'clarification', question: 'Original', createdAt: '2026-09-05',
    error: 'Confirm?', pendingConfirmations: [match],
  }]);
  assert.equal(nodes[0].data.clarificationQuestion, 'Original');
  assert.equal(nodes[0].data.turns.at(-1).content, 'Confirm?');
  assert.deepEqual(nodes[0].data.pendingConfirmations, [match]);
});