const TYPE_ALIASES = {
  bar: 'bar', column: 'bar', sütun: 'bar', cubuk: 'bar',
  line: 'line', çizgi: 'line', cizgi: 'line', area: 'area', alan: 'area',
  pie: 'pie', pasta: 'pie', doughnut: 'doughnut', donut: 'doughnut', halka: 'doughnut',
  radar: 'radar', spider: 'radar', scatter: 'scatter', pareto: 'pareto',
  // Histogram payloads must already contain labelled bins, never raw samples.
  histogram: 'bar',
};
const MIXED_TYPES = ['bar', 'line', 'area'];
const NULLABLE_TYPES = ['bar', 'line', 'area', 'radar'];
const isRecord = value => value !== null && typeof value === 'object' && !Array.isArray(value);
const isLabel = value => typeof value === 'string' || Number.isFinite(value);
const failure = error => ({ valid: false, error });

export default function validateChartPayload(chart) {
  if (!isRecord(chart)) return failure('Grafik verisi bir nesne olmalıdır.');
  const rawType = chart.type ?? chart.chartType;
  const normalizedType = typeof rawType === 'string' ? rawType.toLowerCase().trim() : '';
  const type = Object.hasOwn(TYPE_ALIASES, normalizedType) ? TYPE_ALIASES[normalizedType] : null;
  if (typeof type !== 'string') return failure(`Desteklenmeyen grafik türü: ${String(rawType ?? '(eksik)')}.`);
  if (!isRecord(chart.data)) return failure('Grafik veri gövdesi eksik veya geçersiz.');

  const labels = chart.data.labels === undefined && type === 'scatter' ? [] : chart.data.labels;
  if (!Array.isArray(labels) || !labels.every(isLabel)) return failure('Grafik etiketleri metin veya sonlu sayı olmalıdır.');
  const datasets = chart.data.datasets === undefined && Array.isArray(chart.data.values)
    ? [{ label: 'Veri', data: chart.data.values }] : chart.data.datasets;
  if (!Array.isArray(datasets) || datasets.length === 0) return failure('Grafikte en az bir veri serisi olmalıdır.');
  if (type !== 'scatter' && labels.length === 0) return failure('Grafik etiketleri boş olamaz.');

  for (const dataset of datasets) {
    if (!isRecord(dataset) || !Array.isArray(dataset.data) || dataset.data.length === 0) {
      return failure('Grafik serisi boş veya geçersiz.');
    }
    if (dataset.label != null && typeof dataset.label !== 'string') return failure('Seri adı metin olmalıdır.');
    if (dataset.type != null && (!MIXED_TYPES.includes(type) || !['bar', 'line'].includes(dataset.type))) {
      return failure('Bu grafik için desteklenmeyen karma seri türü.');
    }
    if ((type !== 'scatter' || labels.length > 0) && dataset.data.length !== labels.length) {
      return failure('Etiket sayısı ile her serinin veri sayısı eşit olmalıdır.');
    }
    if (type === 'scatter') {
      if (!dataset.data.every(point => isRecord(point) && Number.isFinite(point.x) && Number.isFinite(point.y))) {
        return failure('Dağılım grafiğinde her noktanın x ve y değerleri sonlu sayı olmalıdır.');
      }
      continue;
    }
    if (!dataset.data.every(value => Number.isFinite(value) || (value === null && NULLABLE_TYPES.includes(type)))) {
      return failure('Grafik değerleri sonlu sayı olmalıdır; bu türde izin veriliyorsa eksik değer null olabilir.');
    }
    if (dataset.data.every(value => value === null)) return failure('Seride gösterilebilecek sayısal değer yok.');
    if (['pie', 'doughnut', 'pareto'].includes(type) && dataset.data.some(value => value < 0)) {
      return failure('Pasta, halka ve Pareto grafiklerinde negatif değer kullanılamaz.');
    }
  }
  if (type === 'pareto') {
    const total = datasets[0].data.reduce((sum, value) => sum + value, 0);
    if (datasets.length !== 1 || !Number.isFinite(total) || total <= 0) {
      return failure('Pareto için toplamı pozitif ve sonlu olan tek bir seri gerekir.');
    }
  }
  return { valid: true, error: null, type, data: { labels, datasets } };
}