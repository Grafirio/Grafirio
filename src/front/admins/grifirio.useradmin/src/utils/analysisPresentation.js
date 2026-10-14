// Clone once on opening: subsequent analysis updates cannot alter an export preview.
const readyForPresentation = node => node.type === 'biAnalysisCard' && !node.data?.loading && node.data?.chart && node.data?.report;

export function presentationAvailability(nodes) {
  const cards = nodes.filter(n => n.type === 'biAnalysisCard');
  const ready = cards.filter(readyForPresentation).length;
  const loading = cards.filter(n => n.data?.loading).length;
  const legacy = cards.filter(n => !n.data?.loading && n.data?.chart && !n.data?.report).length;
  const message = ready ? `${ready} analiz sunuya hazır` : legacy
    ? 'Eski sonuçlarda hesaplama açıklaması yok. İlgili karttan analizi yeniden çalıştırın; tamamlanınca sunuya eklenebilir.'
    : loading ? 'Analiz sürüyor. Sonuç ve hesaplama açıklaması tamamlanınca sunuya eklenebilir.'
    : 'Önce tuvalde bir analiz oluşturun. Sonuç ve hesaplama açıklaması tamamlanınca sunuya eklenebilir.';
  return { ready, loading, legacy, message };
}

export function presentationEntries(nodes) {
  return nodes.filter(readyForPresentation)
    .map(n => structuredClone({ id: n.id, queryId: n.data.resultQueryId || n.data.queryId, chart: n.data.chart,
      charts: n.data.resultCharts || [n.data.chart], chartType: n.data.chartType,
      report: preparePresentationReport(n.data.report, n.data.resultCharts || [n.data.chart], n.data.audit || {}), insights: n.data.insights || [],
      question: n.data.resultQuestion || [...(n.data.turns || [])].reverse().find(t => t.role === 'user')?.content || '' }));
}

export function preparePresentationReport(original, charts, audit = {}) {
  const report = structuredClone(original);
  const chart = charts.find(c => c.data?.datasets?.some(d => d.label === 'Tahmin'));
  if (!chart || !chart.data.datasets.some(d => d.label === 'Gerçekleşen')) return report;
  const { labels, datasets } = chart.data;
  const actual = datasets.find(d => d.label === 'Gerçekleşen').data;
  const predicted = datasets.find(d => d.label === 'Tahmin').data;
  const start = actual.findIndex((v, i) => v == null && Number.isFinite(predicted[i]));
  if (start < 0) return report;
  if (!report.forecastSummary) {
    const values = predicted.slice(start);
    const horizon = values.length;
    const additive = ['sum', 'count'].includes(audit.aggregation);
    const total = additive ? values.reduce((a, b) => a + b, 0) : null;
    const history = actual.slice(0, start);
    const previous = additive && history.length >= horizon && history.slice(-horizon).every(Number.isFinite) ? history.slice(-horizon).reduce((a, b) => a + b, 0) : null;
    const peak = values.indexOf(Math.max(...values));
    const subjectText = report.filters?.map(n => n.match(/^(?:Name|Ad|Adi|Adı|ProductName|CustomerName|ÜrünAdı) = (.+)$/i)?.[1]).find(Boolean);
    const subject = subjectText ? subjectText[0].toLocaleUpperCase('tr-TR') + subjectText.slice(1) : null;
    const metric = audit.aggregation === 'count' ? 'Kayıt sayısı' : /tutar|ciro|gelir/i.test(report.metric || '') ? 'Tutar' : 'Ölçüm';
    report.title = `${subject ? subject + ' — ' : ''}${horizon} aylık ${metric.toLocaleLowerCase('tr-TR')} öngörüsü`;
    report.forecastSummary = { title: report.title, horizon, period: { from: labels[start], to: labels.at(-1) }, total, lastValue: values.at(-1), totalLabel: additive ? `${horizon} aylık toplam öngörü` : null,
      previousTotal: previous, previousLabel: `Önceki ${horizon} gözlenen ayın toplamı`, changePercent: previous ? (total - previous) / Math.abs(previous) * 100 : null, peak: { period: labels[start + peak], value: values[peak] } };
  }
  report.warnings = [...new Set((report.warnings || []).filter(w => !w.includes('Bu bir kesinlik')))];
  if (report.unit === 'Belirtilmedi' && /tutar|ciro|gelir/i.test(report.metric || '') && !report.businessQuestions) {
    report.businessQuestions = ['Tutarların para birimi nedir?', 'Tutar net mi brüt mü; iptal ve iadeler dahil mi?'];
  }
  if (!report.historyPolicy) {
    report.legacyCalculation = true;
    report.forecastSummary.previousTotal = null;
    report.forecastSummary.changePercent = null;
    report.warnings.unshift('Bu eski kayıt tam ay kontrolünden önce oluşturuldu. Güncel ve tamamlanmış aylara dayalı tahmin için analizi yeniden çalıştırın.');
  }
  if (!report.tables?.some(t => t.kind === 'forecast')) {
    const lower = datasets.find(d => d.label?.startsWith('Alt sınır'))?.data || [];
    const upper = datasets.find(d => d.label?.startsWith('Üst sınır'))?.data || [];
    report.tables = [{ title: 'Gerçekleşen geçmiş', kind: 'history', columns: ['Dönem', 'Gerçekleşen'], rows: labels.slice(0, start).map((p, i) => [p, actual[i]]) },
      { title: 'Aylık öngörüler', kind: 'forecast', columns: ['Dönem', 'Tahmin', 'Alt sınır (yaklaşık)', 'Üst sınır (yaklaşık)'], rows: labels.slice(start).map((p, i) => [p, predicted[start + i], lower[start + i] ?? null, upper[start + i] ?? null]) },
      ...(report.tables || []).filter(t => !(t.columns?.includes('Gerçekleşen') && t.columns?.includes('Tahmin')))];
  }
  return report;
}

export function tablePages(table, rowLimit = 12, columnLimit = 5) {
  const pages = [];
  const chunkSize = Math.ceil(table.rows.length / Math.max(1, Math.ceil(table.rows.length / rowLimit)));
  for (let c = 1; c < table.columns.length; c += columnLimit - 1) {
    const indexes = [0, ...Array.from({ length: Math.min(columnLimit - 1, table.columns.length - c) }, (_, i) => c + i)];
    for (let r = 0; r < table.rows.length; r += chunkSize) pages.push({
      title: table.title, columns: indexes.map(i => table.columns[i]),
      rows: table.rows.slice(r, r + chunkSize).map(row => indexes.map(i => row[i])),
      range: `${r + 1}–${Math.min(r + chunkSize, table.rows.length)} / ${table.rows.length}`,
    });
  }
  // Single-column and empty tables still carry their headers.
  if (table.columns.length === 1) {
    for (let r = 0; r < table.rows.length; r += chunkSize) pages.push({ ...table, rows: table.rows.slice(r, r + chunkSize), range: `${r + 1}–${Math.min(r + chunkSize, table.rows.length)} / ${table.rows.length}` });
  }
  return pages.length ? pages : [{ ...table, rows: [] }];
}

export function presentationPages(entries, { includeHistory = false, includeTechnical = false } = {}) {
  const pages = [{ kind: 'cover' }];
  entries.forEach((entry, index) => {
    if (entries.length > 1 && entry.report.forecastSummary) pages.push({ kind: 'overview', entry, index });
    entry.charts.filter(c => c.type !== 'table').forEach((chart, ci) => pages.push({ kind: 'chart', entry, index, chart, ci }));
    (entry.report.tables || []).filter(t => includeHistory || t.kind !== 'history').forEach(table => {
      tablePages(table, table.kind === 'history' ? 18 : 12).forEach(tablePage => pages.push({ kind: 'table', entry, index, tablePage, table }));
    });
    if (includeTechnical) pages.push({ kind: 'technical', entry, index });
  });
  return pages;
}
