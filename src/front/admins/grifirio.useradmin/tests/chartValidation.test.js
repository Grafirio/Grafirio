import test from 'node:test';
import assert from 'node:assert/strict';
import validateChartPayload from '../src/utils/charts/validateChartPayload.js';
import prepareChartDisplay from '../src/utils/charts/prepareChartDisplay.js';

const chart = (type = 'bar', values = [4, 2], labels = ['A', 'B']) => ({
  type, data: { labels, datasets: [{ label: 'Sales', data: values }] },
});

test('supported categorical charts and existing aliases remain valid', () => {
  for (const type of ['bar', 'line', 'area', 'pie', 'doughnut', 'radar', 'pareto', 'histogram', 'donut', 'column']) {
    assert.equal(validateChartPayload(chart(type)).valid, true, type);
  }
  assert.equal(validateChartPayload(chart(' LINE ')).type, 'line');
});

test('unknown and missing chart types never fall back to bar, even with a UI override', () => {
  for (const type of ['waterfall', 'funnel', 'bubble', 'unknown', '', null, {}, 'constructor', '__proto__']) {
    assert.equal(prepareChartDisplay(chart(type), 'bar').valid, false);
  }
  assert.equal(validateChartPayload({ data: chart().data }).valid, false);
});

test('numeric strings, booleans, nonfinite numbers and malformed shapes are rejected', () => {
  for (const value of ['2', '', true, false, NaN, Infinity, -Infinity, undefined, {}, []]) {
    assert.equal(validateChartPayload(chart('bar', [4, value])).valid, false, String(value));
  }
  for (const payload of [null, [], {}, { type: 'bar' }, { type: 'bar', data: [] },
    { type: 'bar', data: { labels: ['A'], datasets: [null] } },
    { type: 'bar', data: { labels: ['A'], datasets: null, values: [1] } },
    { type: 'scatter', data: { labels: null, datasets: [{ data: [{ x: 1, y: 2 }] }] } },
    { type: 'bar', data: { labels: ['A'], datasets: [] } }]) {
    assert.equal(validateChartPayload(payload).valid, false);
  }
});

test('null remains a gap, not zero; unsupported null and all-null series are rejected', () => {
  const payload = chart('line', [0, null]);
  assert.equal(validateChartPayload(payload).valid, true);
  assert.deepEqual(prepareChartDisplay(payload).data.datasets[0].data, [0, null]);
  for (const type of ['pie', 'doughnut', 'pareto', 'scatter']) {
    assert.equal(validateChartPayload(chart(type, [1, null])).valid, false);
  }
  assert.equal(validateChartPayload(chart('bar', [null, null])).valid, false);
});

test('every dataset must align with labels, including multiple series', () => {
  assert.equal(validateChartPayload(chart('bar', [1])).valid, false);
  assert.equal(validateChartPayload(chart('bar', [1, 2], ['A', {}])).valid, false);
  const payload = chart();
  payload.data.datasets.push({ data: [1] });
  assert.equal(validateChartPayload(payload).valid, false);
  payload.data.datasets[1].data = [3, 4];
  assert.equal(validateChartPayload(payload).valid, true);
});

test('legacy values format stays valid without manufacturing missing data', () => {
  assert.equal(validateChartPayload({ type: 'bar', data: { labels: ['A'], values: [1] } }).valid, true);
  assert.equal(validateChartPayload({ type: 'bar', data: { labels: ['A'] } }).valid, false);
});

test('scatter requires finite x/y pairs and optional matching labels', () => {
  const payload = { type: 'scatter', data: { datasets: [{ data: [{ x: 10, y: -2 }, { x: 20, y: 0 }] }] } };
  assert.equal(validateChartPayload(payload).valid, true);
  assert.equal(validateChartPayload({ ...payload, data: { ...payload.data, labels: ['A'] } }).valid, false);
  for (const point of [{ x: '10', y: 2 }, { x: 1, y: null }, { x: Infinity, y: 1 }, null, 2]) {
    assert.equal(validateChartPayload(chart('scatter', [point], ['A'])).valid, false);
  }
});

test('scatter conversion never invents numeric coordinates from category order', () => {
  assert.equal(prepareChartDisplay(chart(), 'scatter').valid, false);
  const converted = prepareChartDisplay(chart('line', [4, 2], [10, 20]), 'scatter');
  assert.deepEqual(converted.data.datasets[0].data, [{ x: 10, y: 4 }, { x: 20, y: 2 }]);
  const back = prepareChartDisplay({ type: converted.type, data: converted.data }, 'bar');
  assert.deepEqual(back.data.labels, [10, 20]);
  assert.deepEqual(back.data.datasets[0].data, [4, 2]);
  assert.equal(prepareChartDisplay(chart('line', [4, null], [10, 20]), 'scatter').valid, false);
});

test('scatter series with different x coordinates cannot become aligned categorical series', () => {
  const payload = { type: 'scatter', data: { datasets: [
    { data: [{ x: 1, y: 2 }] }, { data: [{ x: 2, y: 3 }] },
  ] } };
  assert.equal(prepareChartDisplay(payload, 'line').valid, false);
});

test('Pareto sorts labels and values together without mutation and rejects invalid totals', () => {
  const payload = chart('bar', [2, 8], ['Small', 'Large']);
  const prepared = prepareChartDisplay(payload, 'pareto');
  assert.deepEqual(prepared.data.labels, ['Large', 'Small']);
  assert.deepEqual(prepared.data.datasets[0].data, [8, 2]);
  assert.deepEqual(payload.data.labels, ['Small', 'Large']);
  for (const values of [[0, 0], [-1, 3], [Number.MAX_VALUE, Number.MAX_VALUE]]) {
    assert.equal(validateChartPayload(chart('pareto', values)).valid, false);
  }
  payload.data.datasets.push({ data: [2, 4] });
  assert.equal(prepareChartDisplay(payload, 'pareto').valid, false);
});

test('mixed bar/line remains supported while unknown dataset types are rejected', () => {
  const payload = chart();
  payload.data.datasets.push({ type: 'line', data: [2, 3] });
  assert.equal(validateChartPayload(payload).valid, true);
  payload.data.datasets[1].type = 'waterfall';
  assert.equal(validateChartPayload(payload).valid, false);
});