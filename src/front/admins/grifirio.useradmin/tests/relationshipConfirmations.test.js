import test, { mock } from 'node:test';
import assert from 'node:assert/strict';
import relationshipKey from '../src/utils/relationships/relationshipKey.js';
import getPendingConfirmations from '../src/utils/relationships/getPendingConfirmations.js';
import parseRelationshipDecision from '../src/utils/relationships/parseRelationshipDecision.js';
import applyRelationshipDecisions from '../src/utils/relationships/applyRelationshipDecisions.js';
import getRelationshipResume from '../src/utils/relationships/getRelationshipResume.js';
import getQueryFailure from '../src/utils/getQueryFailure.js';
import restoreFromHistory from '../src/utils/restoreFromHistory.js';

const first = { fromTable: 'dbo.Orders', fromColumn: 'CustomerId', toTable: 'dbo.Customers', toColumn: 'Id' };
const second = { fromTable: 'dbo.Orders', fromColumn: 'ProductId', toTable: 'dbo.Products', toColumn: 'Id' };
const proposals = [first, second];
const applied = { success: true, applied: true };
const clarification = {
  needsRelationshipClarification: true, clarificationQuestion: 'Müşterilere göre toplam gelir?', queryId: 'clarification-id',
};

const saveRelationship = mock.fn(async () => applied);
mock.module('react', { exports: {
  useCallback: callback => callback,
  useRef: current => ({ current }),
} });
mock.module('../src/services/learnRelationship.js', { exports: { default: saveRelationship } });
const { default: useRelationshipConfirmations } = await import('../src/hooks/useRelationshipConfirmations.js');

function createConfirmationHarness(data = {}, ask = async () => {}) {
  saveRelationship.mock.resetCalls();
  let node = { id: 'card:root', data: { queryId: 'root', pendingConfirmations: proposals, turns: [], ...data } };
  const questions = [];
  // eslint-disable-next-line react-hooks/rules-of-hooks -- Ref/callback stubs test handlers without a React renderer.
  const handlers = useRelationshipConfirmations({
    connectionId: 'connection',
    patchCard: (id, patch) => {
      assert.equal(id, node.id);
      node = { ...node, data: { ...node.data, ...(typeof patch === 'function' ? patch(node.data) : patch) } };
    },
    askQuestion: async (card, text) => { questions.push({ node: card, text }); await ask(card, text); },
  });
  return {
    ...handlers, questions,
    get node() { return node; },
    replaceData: patch => { node = { ...node, data: { ...node.data, ...patch } }; },
  };
}

function runDecisions(overrides = {}) {
  const states = {};
  const completed = [];
  const calls = [];
  const promise = applyRelationshipDecisions({
    pending: proposals, matches: proposals, accepted: true,
    save: async (match, accepted) => { calls.push({ match, accepted }); return applied; },
    onState: (key, state) => { states[key] = state; },
    onApplied: (match) => { completed.push(match); },
    ...overrides,
  });
  return { promise, states, completed, calls };
}

test('relationship keys normalize brackets and case without reversing direction', () => {
  assert.equal(relationshipKey({ ...first, fromTable: '[dbo].[Orders]' }), relationshipKey(first));
  assert.notEqual(relationshipKey(first), relationshipKey({
    fromTable: first.toTable, fromColumn: first.toColumn, toTable: first.fromTable, toColumn: first.fromColumn,
  }));
});

test('only structured proposals are eligible, deduplicated and validated at the API boundary', () => {
  assert.deepEqual(getPendingConfirmations({ pendingConfirmations: [first, first, null, {}, { ...second, toColumn: '' }] }), [first]);
  assert.deepEqual(getPendingConfirmations({ answer: 'dbo.Orders.CustomerId -> dbo.Customers.Id' }), []);
  assert.deepEqual(getPendingConfirmations({ pendingConfirmations: 'Orders.CustomerId -> Customers.Id' }), []);
  assert.deepEqual(getPendingConfirmations(null), []);
});

test('explicit empty pending state does not resurrect resolved audit proposals', () => {
  assert.deepEqual(getPendingConfirmations({ pendingConfirmations: [], audit: { pendingConfirmations: proposals } }), []);
});

test('HTTP400 preserves clarification proposals, error message and parent identity', () => {
  const result = getQueryFailure({ response: { status: 400, data: {
    error: 'İlişki onayı gerekiyor', needsClarification: true, queryId: 'query-400', pendingConfirmations: proposals,
  } } });
  assert.equal(result.success, false);
  assert.equal(result.needsClarification, true);
  assert.equal(result.queryId, 'query-400');
  assert.equal(result.error, 'İlişki onayı gerekiyor');
  assert.deepEqual(result.pendingConfirmations, proposals);
});

test('network failures keep their message without manufacturing a confirmation', () => {
  const result = getQueryFailure(new Error('Network Error'));
  assert.equal(result.error, 'Network Error');
  assert.equal(result.needsClarification, false);
  assert.deepEqual(result.pendingConfirmations, []);
});

test('single proposal accepts explicit Turkish approvals and rejections', () => {
  for (const text of ['evet', 'onaylıyorum', ' ONAYLIYORUM! ']) {
    assert.deepEqual(parseRelationshipDecision(text, [first]), { accepted: true, matches: [first], ambiguous: false });
  }
  for (const text of ['hayır', 'reddediyorum', 'HAYIR.']) {
    assert.deepEqual(parseRelationshipDecision(text, [first]), { accepted: false, matches: [first], ambiguous: false });
  }
});

test('generic multi-proposal answers cannot select any relationship', () => {
  for (const text of ['evet', 'onaylıyorum', 'hayır', 'reddediyorum']) {
    const decision = parseRelationshipDecision(text, proposals);
    assert.equal(decision.ambiguous, true);
    assert.deepEqual(decision.matches, []);
  }
});

test('multi-proposal approval and rejection require explicit all wording', () => {
  assert.deepEqual(parseRelationshipDecision('hepsini onaylıyorum', proposals), { accepted: true, matches: proposals, ambiguous: false });
  assert.deepEqual(parseRelationshipDecision('hepsini reddediyorum', proposals), { accepted: false, matches: proposals, ambiguous: false });
});

test('no proposal or non-explicit language is never interpreted as a relationship decision', () => {
  assert.equal(parseRelationshipDecision('evet', []), null);
  assert.equal(parseRelationshipDecision('evet ama CustomerId doğru değil', proposals), null);
  assert.equal(parseRelationshipDecision('Orders.CustomerId ile Customers.Id bağla', proposals), null);
});

test('both decisions use the same save path; only applied approvals permit resume', async () => {
  const approve = runDecisions();
  const result = await approve.promise;
  assert.deepEqual(approve.calls, proposals.map(match => ({ match, accepted: true })));
  assert.deepEqual(approve.completed, proposals);
  assert.deepEqual(getRelationshipResume(clarification, result, false), {
    question: clarification.clarificationQuestion, parentQueryId: clarification.queryId,
  });
  const reject = runDecisions({ accepted: false });
  const rejected = await reject.promise;
  assert.deepEqual(reject.calls, proposals.map(match => ({ match, accepted: false })));
  assert.deepEqual(rejected.pending, []);
  assert.equal(getRelationshipResume(clarification, rejected, true), null);
});

test('one-button approval cannot resume while another proposal remains', async () => {
  const { promise } = runDecisions({ matches: [first] });
  const result = await promise;
  assert.deepEqual(result.pending, [second]);
  assert.equal(getRelationshipResume(clarification, result, false), null);
});

test('HTTP422 leaves the failed match visible; retry only saves the remaining match', async () => {
  const attempt = runDecisions({ save: async (match) => {
    if (match === second) throw { response: { status: 422, data: { error: 'Hedef benzersiz değil', applied: false } } };
    return applied;
  } });
  const result = await attempt.promise;
  assert.deepEqual(result.pending, [second]);
  assert.deepEqual(attempt.completed, [first]);
  assert.equal(attempt.states[relationshipKey(second)].error, 'Hedef benzersiz değil');
  assert.equal(attempt.states[relationshipKey(second)].busy, false);
  assert.equal(getRelationshipResume(clarification, result, false), null);
  const retry = runDecisions({ pending: result.pending, matches: result.pending });
  const retried = await retry.promise;
  assert.deepEqual(retry.calls, [{ match: second, accepted: true }]);
  assert.ok(getRelationshipResume(clarification, retried, false));
});

test('deferred or unsuccessful responses never remove proposals or resume', async () => {
  for (const response of [{ success: true, applied: false }, { success: false, applied: true }, undefined]) {
    const attempt = runDecisions({ save: async () => response });
    const result = await attempt.promise;
    assert.deepEqual(result.pending, proposals);
    assert.deepEqual(attempt.completed, []);
    assert.equal(getRelationshipResume(clarification, result, false), null);
    assert.ok(attempt.states[relationshipKey(first)].error);
  }
});

test('permission failures are visible and cannot be marked answered', async () => {
  const attempt = runDecisions({ save: async () => {
    throw { response: { status: 403, data: { error: 'Yetkiniz yok' } } };
  } });
  assert.deepEqual((await attempt.promise).pending, proposals);
  assert.equal(attempt.states[relationshipKey(first)].error, 'Yetkiniz yok');
});

test('button input not present in structured pending proposals cannot be saved', async () => {
  const attempt = runDecisions({ pending: [first], matches: [second] });
  const result = await attempt.promise;
  assert.deepEqual(attempt.calls, []);
  assert.deepEqual(result.pending, [first]);
});

test('previous rejection, inferred result or missing parent prevents automatic resume', async () => {
  const result = await runDecisions().promise;
  assert.equal(getRelationshipResume(clarification, result, true), null);
  assert.equal(getRelationshipResume({ ...clarification, needsRelationshipClarification: false }, result, false), null);
  assert.equal(getRelationshipResume({ ...clarification, queryId: null }, result, false), null);
});

test('chartless history restores structured proposals and original question for resume', () => {
  const { nodes } = restoreFromHistory([{
    queryId: 'root', status: 'clarification', question: 'Özgün soru',
    clarificationQuestion: 'Bu ilişkiyi onaylıyor musunuz?', createdAt: '2026-09-05',
    llmParameters: { relationship_proposals: proposals },
  }]);
  assert.equal(nodes.length, 1);
  assert.equal(nodes[0].data.chart, null);
  assert.deepEqual(nodes[0].data.pendingConfirmations, proposals);
  assert.equal(nodes[0].data.needsRelationshipClarification, true);
  assert.equal(nodes[0].data.clarificationQuestion, 'Özgün soru');
  assert.equal(nodes[0].data.queryId, 'root');
});

test('resumed history keeps one card and clears obsolete clarification proposals', () => {
  const { nodes } = restoreFromHistory([
    { queryId: 'root', status: 'clarification', question: 'Soru', createdAt: '2026-09-05', llmParameters: { relationship_proposals: proposals } },
    { queryId: 'child', parentQueryId: 'root', status: 'completed', question: 'Soru', createdAt: '2026-09-05', result: { summary: 'Tamamlandı' } },
  ]);
  assert.equal(nodes.length, 1);
  assert.equal(nodes[0].id, 'card:root');
  assert.equal(nodes[0].data.queryId, 'child');
  assert.equal(nodes[0].data.needsRelationshipClarification, false);
  assert.deepEqual(nodes[0].data.pendingConfirmations, []);
});

test('completed history restores inferred confirmations without auto-resume eligibility', () => {
  const { nodes } = restoreFromHistory([{
    queryId: 'done', status: 'completed', question: 'Soru', createdAt: '2026-09-05',
    result: { charts: [{ type: 'bar' }], audit: { pendingConfirmations: [first] } },
  }]);
  assert.deepEqual(nodes[0].data.pendingConfirmations, [first]);
  assert.equal(nodes[0].data.needsRelationshipClarification, false);
  assert.equal(nodes[0].data.chart.type, 'bar');
});

test('active dictionary approvals remove completed decisions from chartless history without running a query', () => {
  const { nodes } = restoreFromHistory([{
    queryId: 'root', status: 'clarification', question: 'Özgün soru', createdAt: '2026-09-05',
    llmParameters: { relationship_proposals: proposals },
  }], Object.fromEntries(proposals.map(match => [relationshipKey(match), true])));
  const harness = createConfirmationHarness(nodes[0].data);
  assert.equal(harness.node.data.chart, null);
  assert.deepEqual(harness.node.data.pendingConfirmations, []);
  assert.equal(harness.node.data.needsRelationshipClarification, false);
  assert.equal(harness.node.data.clarificationQuestion, null);
  assert.equal(harness.node.data.confirmationRejected, false);
  assert.equal(harness.node.data.queryId, 'root');
  assert.deepEqual(harness.questions, []);
  assert.equal(saveRelationship.mock.callCount(), 0);
});

test('mixed applied decisions are removed but any original rejection is remembered', () => {
  const { nodes } = restoreFromHistory([{
    queryId: 'root', status: 'clarification', question: 'Soru', createdAt: '2026-09-05',
    llmParameters: { relationship_proposals: proposals },
  }], { [relationshipKey(first)]: true, [relationshipKey(second)]: false });
  assert.deepEqual(nodes[0].data.pendingConfirmations, []);
  assert.equal(nodes[0].data.confirmationRejected, true);
  assert.equal(nodes[0].data.needsRelationshipClarification, false);
});

test('restored rejection prevents auto-resume when the remaining proposal is approved', async () => {
  const { nodes } = restoreFromHistory([{
    queryId: 'root', status: 'clarification', question: 'Soru', createdAt: '2026-09-05',
    llmParameters: { relationship_proposals: proposals },
  }], { [relationshipKey(first)]: false });
  assert.deepEqual(nodes[0].data.pendingConfirmations, [second]);
  assert.equal(nodes[0].data.confirmationRejected, true);
  assert.equal(nodes[0].data.needsRelationshipClarification, true);
  const harness = createConfirmationHarness(nodes[0].data);
  await harness.handleAsk(harness.node, 'onaylıyorum');
  assert.equal(saveRelationship.mock.callCount(), 1);
  assert.deepEqual(harness.node.data.pendingConfirmations, []);
  assert.deepEqual(harness.questions, []);
  assert.ok(harness.node.data.turns.at(-1).content.includes('otomatik çalıştırılmadı'));
});

test('partial approval restores only undecided matches and resumes only after an explicit new approval', async () => {
  const { nodes } = restoreFromHistory([{
    queryId: 'root', status: 'clarification', question: 'Özgün soru', createdAt: '2026-09-05',
    llmParameters: { relationship_proposals: [{ ...first, fromTable: '[dbo].[Orders]' }, second] },
  }], { [relationshipKey(first)]: true });
  const harness = createConfirmationHarness(nodes[0].data);
  assert.deepEqual(harness.node.data.pendingConfirmations, [second]);
  assert.deepEqual(harness.questions, []);
  await harness.handleAsk(harness.node, 'evet');
  assert.deepEqual(saveRelationship.mock.calls[0].arguments, ['connection', second, true, 'Özgün soru']);
  assert.equal(harness.questions.length, 1);
  assert.equal(harness.questions[0].text, 'Özgün soru');
  assert.equal(harness.questions[0].node.data.queryId, 'root');
});

test('completed history also filters decisions from audit proposals without dropping its chart', () => {
  const { nodes } = restoreFromHistory([{
    queryId: 'done', status: 'completed', question: 'Soru', createdAt: '2026-09-05',
    result: { charts: [{ type: 'bar' }], audit: { pendingConfirmations: proposals } },
  }], { [relationshipKey(first)]: true, [relationshipKey(second)]: false });
  assert.deepEqual(nodes[0].data.pendingConfirmations, []);
  assert.equal(nodes[0].data.confirmationRejected, true);
  assert.equal(nodes[0].data.needsRelationshipClarification, false);
  assert.equal(nodes[0].data.chart.type, 'bar');
});

test('missing or non-boolean decisions leave proposals pending and unrelated rejections do not taint a card', () => {
  const history = [{
    queryId: 'root', status: 'clarification', question: 'Soru', createdAt: '2026-09-05',
    llmParameters: { relationship_proposals: [first] },
  }];
  for (const decision of [undefined, null, 'true', 'false', { accepted: true }]) {
    const { nodes } = restoreFromHistory(history, {
      [relationshipKey(first)]: decision, [relationshipKey(second)]: false,
    });
    assert.deepEqual(nodes[0].data.pendingConfirmations, [first]);
    assert.equal(nodes[0].data.confirmationRejected, false);
  }
});

test('a later question replaces the restored rejection state of its parent', () => {
  const { nodes } = restoreFromHistory([
    { queryId: 'root', status: 'clarification', question: 'Soru', createdAt: '2026-09-05', llmParameters: { relationship_proposals: [first] } },
    { queryId: 'child', parentQueryId: 'root', status: 'clarification', question: 'Yeni soru', createdAt: '2026-09-05', llmParameters: { relationship_proposals: [second] } },
  ], { [relationshipKey(first)]: false });
  assert.equal(nodes.length, 1);
  assert.equal(nodes[0].data.queryId, 'child');
  assert.deepEqual(nodes[0].data.pendingConfirmations, [second]);
  assert.equal(nodes[0].data.confirmationRejected, false);
  assert.equal(nodes[0].data.clarificationQuestion, 'Yeni soru');
});

test('pending proposals do not block pair corrections or a different question', async () => {
  for (const text of ['evet ama CustomerId doğru değil', 'Orders.CustomerId ile Customers.Id bağla', 'Ürünlere göre satışları göster']) {
    const harness = createConfirmationHarness();
    const original = harness.node;
    await harness.handleAsk(original, text);
    assert.deepEqual(harness.questions, [{ node: original, text }]);
    assert.equal(saveRelationship.mock.callCount(), 0);
    assert.deepEqual(harness.node.data.turns, []);
  }
});

test('ambiguous explicit multi-proposal answers explain selection instead of approving or querying', async () => {
  const harness = createConfirmationHarness();
  await harness.handleAsk(harness.node, 'evet');
  assert.equal(saveRelationship.mock.callCount(), 0);
  assert.deepEqual(harness.questions, []);
  assert.deepEqual(harness.node.data.pendingConfirmations, proposals);
  assert.equal(harness.node.data.turns[0].content, 'evet');
  assert.ok(harness.node.data.turns[1].content.includes('Bir eşleşmenin düğmesini kullanın'));
});

test('stale card snapshots use session pending state and cannot save applied relationships again', async () => {
  const harness = createConfirmationHarness(clarification);
  const staleNode = harness.node;
  await harness.handleConfirmMatch(staleNode, first, true);
  await harness.handleAsk(staleNode, 'evet');
  assert.deepEqual(saveRelationship.mock.calls.map(call => call.arguments[1]), proposals);
  assert.equal(harness.questions.length, 1);
  await harness.handleConfirmMatch(staleNode, first, true);
  await harness.handleConfirmMatch(staleNode, second, false);
  await harness.handleAsk(staleNode, 'onaylıyorum');
  assert.equal(saveRelationship.mock.callCount(), 2);
  assert.equal(harness.questions.at(-1).text, 'onaylıyorum');
});

test('a new free-text question clears cached pending and rejection even if its query id is unchanged', async () => {
  const harness = createConfirmationHarness(clarification);
  await harness.handleConfirmMatch(harness.node, first, false);
  await harness.handleAsk(harness.node, 'Düzeltilmiş soru');
  harness.replaceData({ pendingConfirmations: [first], confirmationRejected: false, clarificationQuestion: 'Düzeltilmiş soru' });
  await harness.handleAsk(harness.node, 'evet');
  assert.deepEqual(saveRelationship.mock.calls.map(call => call.arguments.slice(1, 3)), [[first, false], [first, true]]);
  assert.deepEqual(harness.questions.map(question => question.text), ['Düzeltilmiş soru', 'Düzeltilmiş soru']);
});

test('fresh query ids isolate new proposals from the previous completed session', async () => {
  const harness = createConfirmationHarness({ ...clarification, pendingConfirmations: [first] });
  const oldNode = harness.node;
  await harness.handleAsk(oldNode, 'evet');
  harness.replaceData({ queryId: 'next-query', pendingConfirmations: [second], needsRelationshipClarification: true });
  await harness.handleAsk(harness.node, 'evet');
  assert.deepEqual(saveRelationship.mock.calls.map(call => call.arguments[1]), proposals);
  assert.deepEqual(harness.questions.map(question => question.node.data.queryId), ['clarification-id', 'next-query']);
  await harness.handleConfirmMatch(oldNode, first, true);
  assert.equal(saveRelationship.mock.callCount(), 2);
});

test('in-flight questions block stale approvals and release the guard after failure', async () => {
  let rejectQuestion;
  const harness = createConfirmationHarness({}, () => new Promise((resolve, reject) => { rejectQuestion = reject; }));
  const staleNode = harness.node;
  const asking = harness.handleAsk(staleNode, 'Yeni soru');
  await harness.handleConfirmMatch(staleNode, first, true);
  await harness.handleAsk(staleNode, 'evet');
  assert.equal(saveRelationship.mock.callCount(), 0);
  assert.equal(harness.questions.length, 1);
  rejectQuestion(new Error('Network Error'));
  await assert.rejects(asking, /Network Error/);
  await harness.handleConfirmMatch(staleNode, first, true);
  assert.equal(saveRelationship.mock.callCount(), 1);
});