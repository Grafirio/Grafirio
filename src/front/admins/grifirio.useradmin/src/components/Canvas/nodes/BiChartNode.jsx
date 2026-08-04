import React, { useState, useEffect } from 'react';
import {
  Chart as ChartJS,
  CategoryScale, LinearScale, PointElement, LineElement,
  BarElement, ArcElement, RadialLinearScale,
  Title, Tooltip, Legend, Filler
} from 'chart.js';
import { Bar, Line, Pie, Doughnut, Radar, Scatter } from 'react-chartjs-2';

ChartJS.register(
  CategoryScale, LinearScale, PointElement, LineElement,
  BarElement, ArcElement, RadialLinearScale,
  Title, Tooltip, Legend, Filler
);

// Chart.js paints to a canvas and cannot read CSS custom properties, so the
// brand chart palette (tokens.css --gf-c01…c12) is mirrored here as literals.
// The order is fixed by the brand guide and must not be reshuffled: the same
// series has to keep the same colour wherever it is drawn.
const PALETTE = [
  'rgba(14,143,140,0.8)',  // --gf-c01 teal
  'rgba(26,122,156,0.8)',  // --gf-c02
  'rgba(47,95,168,0.8)',   // --gf-c03
  'rgba(75,74,159,0.8)',   // --gf-c04
  'rgba(107,58,151,0.8)',  // --gf-c05
  'rgba(138,46,142,0.8)',  // --gf-c06
  'rgba(168,42,124,0.8)',  // --gf-c07
  'rgba(196,46,110,0.8)',  // --gf-c08
  'rgba(214,69,80,0.8)',   // --gf-c09
  'rgba(228,99,60,0.8)',   // --gf-c10
  'rgba(240,144,43,0.8)',  // --gf-c11
  'rgba(248,198,48,0.8)',  // --gf-c12 sun
];
const PALETTE_BORDER = PALETTE.map(c => c.replace('0.8)', '1)'));

/** AI'dan gelen ham dataset'leri Chart.js formatına normalize et */
function buildDatasets(rawDatasets, chartType) {
  if (!rawDatasets?.length) return [];

  return rawDatasets.map((ds, i) => {
    const isLine = ['line', 'area'].includes((ds.type || chartType).toLowerCase());
    const base = {
      label:       ds.label || `Seri ${i + 1}`,
      data:        ds.data  || [],
      type:        ds.type  || undefined,   // mixed chart desteği (Pareto)
      order:       ds.order ?? i,
      borderWidth: isLine ? 2 : 1,
      tension:     0.4,
      fill:        ds.fill  ?? (chartType === 'area'),
    };

    // Renk her zaman buradaki paletten gelir. Backend de bir palet gonderiyor
    // ama sunum karari istemcinin: aksi halde tema degistiginde grafikler eski
    // renklerde kalir ve tuval geri kalan arayuzle uyumsuz gorunur.
    if (isLine) {
      base.backgroundColor = 'rgba(54,69,79,0.12)';
      base.borderColor     = PALETTE_BORDER[i % PALETTE_BORDER.length];
      base.pointBackgroundColor = PALETTE_BORDER[i % PALETTE_BORDER.length];
    } else if (['pie', 'doughnut', 'donut'].includes(chartType)) {
      base.backgroundColor = PALETTE;
      base.borderColor     = '#fff';
    } else {
      base.backgroundColor = PALETTE[i % PALETTE.length];
      base.borderColor     = PALETTE_BORDER[i % PALETTE_BORDER.length];
    }

    return base;
  });
}

/** Tip normalizasyonu: bilinen tüm türleri canonical forma çevir */
function normalizeType(raw) {
  const t = (raw || 'bar').toLowerCase().trim();
  const MAP = {
    'bar': 'bar', 'column': 'bar', 'sütun': 'bar', 'cubuk': 'bar',
    'line': 'line', 'çizgi': 'line', 'cizgi': 'line',
    'area': 'area', 'alan': 'area',
    'pie': 'pie', 'pasta': 'pie',
    'doughnut': 'doughnut', 'donut': 'doughnut', 'halka': 'doughnut',
    'radar': 'radar', 'spider': 'radar',
    'scatter': 'scatter', 'bubble': 'scatter',
    'pareto': 'pareto',
    'histogram': 'bar',
    'waterfall': 'bar', 'funnel': 'bar',
  };
  return MAP[t] || 'bar';
}

export default function BiChartNode({ data }) {
  // ── Timeout state (3 dk — aiChatService polling penceresiyle eşleşir) ──
  const [timedOut, setTimedOut] = useState(false);
  useEffect(() => {
    if (!data?.loading) { setTimedOut(false); return; }
    const t = setTimeout(() => setTimedOut(true), 180_000);
    return () => clearTimeout(t);
  }, [data?.loading]);

  // ── Loading state ──────────────────────────────────────────────────
  if (data?.loading) {
    return (
      <div className="bi-node bi-chart-node">
        <div className="bi-node-header">
          <span className="bi-node-icon">📊</span>
          <span className="bi-node-title">{data?.title || 'Grafik Hazırlanıyor...'}</span>
          <span className="bi-node-type-badge">Chart</span>
        </div>
        <div className="bi-chart-loading-body">
          {timedOut ? (
            <>
              <span style={{ fontSize: 28 }}>⏱️</span>
              <span className="bi-chart-loading-text" style={{ color: 'var(--warning)' }}>
                Yanıt gecikmeli. Tekrar soru sorabilirsiniz.
              </span>
            </>
          ) : (
            <>
              <div className="bi-chart-spinner" />
              <span className="bi-chart-loading-text">AI veri analiz ediyor...</span>
            </>
          )}
        </div>
      </div>
    );
  }

  const rawType    = data?.type || data?.chartType || 'bar';
  const chartType  = normalizeType(rawType);
  const labels     = data?.data?.labels || [];
  const rawDatasets = data?.data?.datasets;

  // ── Pareto: bar + kümülatif çizgi ─────────────────────────────────
  // Pareto özel hazırlık — tek dataset geliyorsa kümülatif çizgiyi hesapla
  let finalDatasets;
  if (chartType === 'pareto' && rawDatasets?.length === 1) {
    const vals    = rawDatasets[0].data || [];
    const total   = vals.reduce((s, v) => s + Number(v), 0) || 1;
    let cum       = 0;
    const cumData = vals.map(v => { cum += Number(v); return +((cum / total) * 100).toFixed(1); });

    finalDatasets = [
      {
        ...buildDatasets(rawDatasets, 'bar')[0],
        type: 'bar',
        backgroundColor: PALETTE[0],
        borderColor:     PALETTE_BORDER[0],
        yAxisID: 'y',
      },
      {
        label: 'Kümülatif %',
        data: cumData,
        type: 'line',
        borderColor: PALETTE_BORDER[3],
        backgroundColor: 'transparent',
        borderWidth: 2,
        pointRadius: 3,
        tension: 0.3,
        yAxisID: 'y2',
      },
    ];
  } else {
    finalDatasets = buildDatasets(rawDatasets, chartType);
    if (!finalDatasets.length) {
      // Eski format uyumluluğu: flat values array
      const values = data?.data?.values || [];
      finalDatasets = [{
        label: 'Veri',
        data: values,
        backgroundColor: ['pie','doughnut','donut'].includes(chartType) ? PALETTE : PALETTE[0],
        borderColor: '#fff',
        borderWidth: 1,
      }];
    }
  }

  const chartData = { labels, datasets: finalDatasets };

  const scaleDefaults = {
    y:  { beginAtZero: true, grid: { color: 'rgba(0,0,0,0.05)' } },
    x:  { grid: { display: false } },
  };

  const options = {
    responsive: true,
    maintainAspectRatio: false,
    plugins: {
      legend: { display: true, position: 'top', labels: { font: { size: 11 } } },
      tooltip: { backgroundColor: 'rgba(15,23,42,0.9)', padding: 10 },
    },
    scales: ['pie', 'doughnut', 'donut', 'radar', 'scatter'].includes(chartType) ? {} :
            chartType === 'pareto' ? {
              y:  { ...scaleDefaults.y, position: 'left',  title: { display: true, text: 'Adet' } },
              y2: { beginAtZero: true, max: 100, position: 'right', grid: { drawOnChartArea: false }, ticks: { callback: v => v + '%' } },
              x:  scaleDefaults.x,
            } : scaleDefaults,
  };

  const renderChart = () => {
    // Pareto veya mixed: Bar ile render et (datasets içinde type tanımlı)
    if (chartType === 'pareto' || finalDatasets.some(d => d.type)) {
      return <Bar data={chartData} options={options} />;
    }
    switch (chartType) {
      case 'bar':      return <Bar     data={chartData} options={options} />;
      case 'line':
      case 'area':     return <Line    data={chartData} options={options} />;
      case 'pie':      return <Pie     data={chartData} options={options} />;
      case 'doughnut':
      case 'donut':    return <Doughnut data={chartData} options={options} />;
      case 'radar':    return <Radar   data={chartData} options={options} />;
      case 'scatter':  return <Scatter data={chartData} options={{ ...options, scales: { x: { grid: { color: 'rgba(0,0,0,0.05)' } }, y: { beginAtZero: true, grid: { color: 'rgba(0,0,0,0.05)' } } } }} />;
      default:         return <Bar     data={chartData} options={options} />;
    }
  };

  return (
    <div className="bi-node bi-chart-node">
      <div className="bi-node-header">
        <span className="bi-node-icon">📊</span>
        <span className="bi-node-title">{data?.title || 'Grafik'}</span>
        <span className="bi-node-type-badge">Chart</span>
      </div>
      <div className="bi-chart-body">
        {renderChart()}
      </div>
    </div>
  );
}
