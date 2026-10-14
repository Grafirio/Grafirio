import test from 'node:test';
import assert from 'node:assert/strict';
import { presentationEntries, presentationAvailability, tablePages } from '../src/utils/analysisPresentation.js';
import resolveAgentResponse from '../src/utils/resolveAgentResponse.js';
import restoreFromHistory from '../src/utils/restoreFromHistory.js';

test('export availability distinguishes legacy, running and empty results using the export contract', () => {
  const ready = { type: 'biAnalysisCard', data: { chart: {}, report: {} } };
  const legacy = { type: 'biAnalysisCard', data: { chart: {} } };
  const running = { type: 'biAnalysisCard', data: { ...ready.data, loading: true } };
  const all = [ready, legacy, running, { type: 'other', data: ready.data }];
  assert.equal(presentationAvailability(all).ready, presentationEntries(all).length);
  assert.match(presentationAvailability([legacy]).message, /yeniden çalıştırın/);
  assert.match(presentationAvailability([running]).message, /Analiz sürüyor/);
  assert.match(presentationAvailability([]).message, /Önce tuvalde/);
});

test('export freezes result and preserves its identity after a failed follow-up', () => {
  const node = { id: 'one', type: 'biAnalysisCard', data: { queryId: 'failed-followup', resultQueryId: 'original', resultQuestion: 'Original question', chart: { title: 'Saved' }, report: { method: 'SUM', tables: [] } } };
  const entries = presentationEntries([node, { ...node, id: 'loading', data: { ...node.data, loading: true } }]);
  assert.equal(entries.length, 1);
  assert.equal(entries[0].queryId, 'original');
  assert.equal(entries[0].question, 'Original question');
  node.data.report.method = 'changed';
  assert.equal(entries[0].report.method, 'SUM');
});

test('long wide tables keep all cells across pages with repeated identity column', () => {
  const table = { columns: ['Period', ...Array.from({ length: 9 }, (_, i) => `C${i}`)], rows: Array.from({ length: 37 }, (_, i) => [i, ...Array.from({ length: 9 }, (_, c) => `${i}-${c}`)]) };
  const pages = tablePages(table);
  assert.equal(pages.length, 12);
  for (const row of table.rows) for (let c = 1; c < row.length; c++) {
    assert(pages.some(p => p.columns.includes(table.columns[c]) && p.rows.some(r => r[0] === row[0] && r[p.columns.indexOf(table.columns[c])] === row[c])));
  }
  assert(pages.every(p => p.rows.length <= 12 && p.columns.length <= 5));
});

test('report and warnings survive transport and history restoration', () => {
  const report = { version: 'analysis-report-v1', tables: [], notices: ['5 yıl istendi; 12 ay gösteriliyor.'] };
  const result = { charts: [{ type: 'bar' }], report, insights: [{ type: 'warning', description: 'Partial' }] };
  assert.deepEqual(resolveAgentResponse({ status: 'completed', result }, 'q').report, report);
  const restored = restoreFromHistory([{ queryId: 'q', status: 'completed', question: 'Total', result }]).nodes[0].data;
  assert.deepEqual(restored.report, report);
  assert.equal(restored.insights.length, 1);
  assert.equal(restored.resultQueryId, 'q');
  const followed = restoreFromHistory([
    { queryId: 'q', status: 'completed', question: 'Total', result },
    { queryId: 'followup', parentQueryId: 'q', status: 'completed', question: 'Explain', result: { charts: [], summary: 'Explanation' } },
  ]).nodes[0].data;
  assert.equal(followed.queryId, 'followup');
  assert.equal(followed.resultQueryId, 'q');
  assert.deepEqual(followed.report, report);
});
