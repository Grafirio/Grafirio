// Clone once on opening: subsequent analysis updates cannot alter an export preview.
export function presentationEntries(nodes) {
  return nodes.filter(n => n.type === 'biAnalysisCard' && !n.data?.loading && n.data?.chart && n.data?.report)
    .map(n => structuredClone({ id: n.id, queryId: n.data.resultQueryId || n.data.queryId, chart: n.data.chart,
      charts: n.data.resultCharts || [n.data.chart], chartType: n.data.chartType,
      report: n.data.report, insights: n.data.insights || [],
      question: n.data.resultQuestion || [...(n.data.turns || [])].reverse().find(t => t.role === 'user')?.content || '' }));
}

export function tablePages(table, rowLimit = 12, columnLimit = 5) {
  const pages = [];
  for (let c = 1; c < table.columns.length; c += columnLimit - 1) {
    const indexes = [0, ...Array.from({ length: Math.min(columnLimit - 1, table.columns.length - c) }, (_, i) => c + i)];
    for (let r = 0; r < table.rows.length; r += rowLimit) pages.push({
      title: table.title, columns: indexes.map(i => table.columns[i]),
      rows: table.rows.slice(r, r + rowLimit).map(row => indexes.map(i => row[i])),
      range: `${r + 1}–${Math.min(r + rowLimit, table.rows.length)} / ${table.rows.length}`,
    });
  }
  // Single-column and empty tables still carry their headers.
  if (table.columns.length === 1) {
    for (let r = 0; r < table.rows.length; r += rowLimit) pages.push({ ...table, rows: table.rows.slice(r, r + rowLimit), range: `${r + 1}–${Math.min(r + rowLimit, table.rows.length)} / ${table.rows.length}` });
  }
  return pages.length ? pages : [{ ...table, rows: [] }];
}
