import validateChartPayload from './validateChartPayload.js';

export default function prepareChartDisplay(chart, displayType) {
  // Validate the source before applying a UI preference, so an invalid type cannot be hidden.
  const source = validateChartPayload(chart);
  if (!source.valid) return source;
  const target = displayType ?? source.type;
  let { labels, datasets } = source.data;

  if (target === 'scatter' && source.type !== 'scatter') {
    if (!labels.every(Number.isFinite)) {
      return { valid: false, error: 'Dağılıma dönüşüm için sayısal x etiketleri gerekir; kategori sırası ölçüm yerine kullanılamaz.' };
    }
    datasets = datasets.map(dataset => ({
      label: dataset.label,
      data: dataset.data.map((value, index) => ({ x: labels[index], y: value })),
    }));
  } else if (source.type === 'scatter' && target !== 'scatter') {
    const coordinates = datasets[0].data.map(point => point.x);
    if (!datasets.every(dataset => dataset.data.length === coordinates.length
      && dataset.data.every((point, index) => point.x === coordinates[index]))) {
      return { valid: false, error: 'Bu dönüşüm için dağılım serilerinin x değerleri ve sırası aynı olmalıdır.' };
    }
    labels = labels.length ? labels : coordinates;
    datasets = datasets.map(dataset => ({ label: dataset.label, data: dataset.data.map(point => point.y) }));
  }

  const prepared = validateChartPayload({ type: target, data: { labels, datasets } });
  if (!prepared.valid || prepared.type !== 'pareto') return prepared;
  const order = labels.map((_, index) => index).sort((left, right) => datasets[0].data[right] - datasets[0].data[left]);
  return {
    ...prepared,
    data: {
      labels: order.map(index => labels[index]),
      datasets: [{ ...datasets[0], data: order.map(index => datasets[0].data[index]) }],
    },
  };
}