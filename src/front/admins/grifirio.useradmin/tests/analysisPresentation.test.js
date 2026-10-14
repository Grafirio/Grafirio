import test from 'node:test';
import assert from 'node:assert/strict';
import { presentationEntries, presentationAvailability, tablePages, presentationPages, preparePresentationReport } from '../src/utils/analysisPresentation.js';
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

test('balances table pages instead of leaving a single orphan row', () => {
  const table = { columns: ['Month', 'Value'], rows: Array.from({length:25}, (_, i) => [i, i]) };
  assert.deepEqual(tablePages(table, 12).map(p => p.rows.length), [9, 9, 7]);
});

test('legacy forecast becomes a three-page report without modifying its calculation', () => {
  const chart = { type: 'line', title: 'SUM(LineTotal)', data: { labels: Array.from({length:37}, (_, i) => String(i)),
    datasets: [{label:'Gerçekleşen', data:Array.from({length:37}, (_, i) => i<25 ? 100 : null)},
               {label:'Tahmin', data:Array.from({length:37}, (_, i) => i>=24 ? 200 : null)}] } };
  const original = { metric:'Satırın toplam tutarı', unit:'Belirtilmedi', filters:['Name = Test ürün'], warnings:[], tables:[] };
  const report = preparePresentationReport(original, [chart], {aggregation:'sum'});
  assert.equal(report.forecastSummary.total, 2400);
  assert.equal(report.forecastSummary.previousTotal, null);
  assert.equal(report.forecastSummary.changePercent, null);
  assert.match(report.title, /Test ürün/);
  assert.match(report.warnings[0], /tam ay kontrolünden önce/);
  assert.deepEqual(report.tables.map(t => t.rows.length), [25,12]);
  const entries = [{report,charts:[chart]}];
  assert.deepEqual(presentationPages(entries).map(p => p.kind), ['cover','chart','table']);
  assert.equal(presentationPages(entries, {includeHistory:true,includeTechnical:true}).length, 6);
  assert.deepEqual(original.tables, []);
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
