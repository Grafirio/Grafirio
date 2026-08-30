import React, { useState, useEffect, useRef, useMemo } from 'react';
import {
  Chart as ChartJS,
  CategoryScale, LinearScale, PointElement, LineElement,
  BarElement, ArcElement, RadialLinearScale,
  Title, Tooltip, Legend, Filler
} from 'chart.js';
import { Bar, Line, Pie, Doughnut, Radar, Scatter } from 'react-chartjs-2';
import {
  resolveTheme, BAR_RADIUS, BAR_PERCENTAGE, CATEGORY_PERCENTAGE,
} from './chartTheme';
import NodeComposer from './NodeComposer';

ChartJS.register(
  CategoryScale, LinearScale, PointElement, LineElement,
  BarElement, ArcElement, RadialLinearScale,
  Title, Tooltip, Legend, Filler
);

/** Kimlik taşıyan formlar: her dilim/segment ayrı bir şeyi temsil eder. */
const IDENTITY_TYPES = ['pie', 'doughnut', 'donut', 'radar'];

const withAlpha = (hex, alpha) => {
  const n = parseInt(hex.slice(1), 16);
  return `rgba(${(n >> 16) & 255}, ${(n >> 8) & 255}, ${n & 255}, ${alpha})`;
};

/**
 * AI'dan gelen ham dataset'leri Chart.js formatına çevirir.
 *
 * Renk kuralı: kimliği renk taşıyorsa kategorik palet, taşımıyorsa tek ton.
 * Tek serili bir çubuk grafikte her çubuğu farklı renge boyamak bilgi
 * eklemez — çubuğun boyu değeri zaten anlatıyor — ama renk ile sıralama
 * arasında olmayan bir ilişki kurulduğu izlenimi verir.
 */
function buildDatasets(rawDatasets, chartType, theme) {
  if (!rawDatasets?.length) return [];

  const palette = theme.categorical;
  const multiSeries = rawDatasets.length > 1;
  const sliceColored = IDENTITY_TYPES.includes(chartType);

  return rawDatasets.map((ds, i) => {
    const isLine = ['line', 'area'].includes((ds.type || chartType).toLowerCase());
    const hue = multiSeries ? palette[i % palette.length] : theme.primary;

    const base = {
      label: ds.label || `Seri ${i + 1}`,
      data: ds.data || [],
      type: ds.type || undefined, // mixed chart desteği (Pareto)
      order: ds.order ?? i,
      borderWidth: isLine ? 2 : 0,
      tension: 0.35,
      fill: ds.fill ?? (chartType === 'area'),
    };

    if (isLine) {
      base.borderColor = hue;
      base.backgroundColor = chartType === 'area' ? withAlpha(hue, 0.14) : 'transparent';
      base.pointBackgroundColor = hue;
      base.pointBorderColor = theme.surface;
      base.pointBorderWidth = 2;
      base.pointRadius = 4;
      base.pointHoverRadius = 6;
    } else if (sliceColored) {
      // Dilimlerin her biri ayrı bir kategori: kimlik rengi burada anlamlı.
      base.backgroundColor = palette;
      // Zemin renginde ince ayraç — bitişik dilimler birbirine karışmasın.
      base.borderColor = theme.surface;
      base.borderWidth = 2;
    } else {
      base.backgroundColor = hue;
      base.borderRadius = BAR_RADIUS;
      base.borderSkipped = false;
      base.hoverBackgroundColor = withAlpha(hue, 0.82);
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

/** Uzun kategori adları ekseni boğmasın. */
const truncate = (value, max = 18) => {
  const s = String(value);
  return s.length > max ? `${s.slice(0, max - 1)}…` : s;
};

/**
 * Düğüm başlığındaki eylemler.
 *
 * Ayrı bir bileşen: hem grafik hem yükleme durumunda aynı satır çiziliyor,
 * yükleme sırasında da silinebilmeli — takılı kalmış bir analiz kutusunun
 * tuvalde kalıcı olması için bir sebep yok.
 */
function ChartActions({ onToggleRefine, refineOpen, onDelete }) {
  return (
    <>
      {onToggleRefine && (
        <button
          type="button"
          className={`bi-node-action${refineOpen ? ' is-active' : ''}`}
          title="Bu grafiği yeniden sor"
          onMouseDown={(e) => e.stopPropagation()}
          onClick={(e) => { e.stopPropagation(); onToggleRefine(); }}
        >
          ✎
        </button>
      )}
      {onDelete && (
        <button
          type="button"
          className="bi-node-action bi-node-action--danger"
          title="Bu grafiği sil"
          onMouseDown={(e) => e.stopPropagation()}
          onClick={(e) => { e.stopPropagation(); onDelete(); }}
        >
          🗑
        </button>
      )}
    </>
  );
}

/**
 * Sonucun altında duran eşleştirme onayı.
 *
 * Sistem iki tabloyu adlarına bakıp tahminle bağladığında sonuç yine de
 * gösteriliyor, ama sorusuyla birlikte. Sıra bilinçli: kullanıcı kolon
 * eşleşmesini değerlendiremez, CEVABI değerlendirebilir — "bu firmalar
 * doğru mu" cevaplanabilir bir soru, "ReferanceId ile ReferenceId aynı mı"
 * değil.
 *
 * Sonucu göstermenin güvenli olmasının şartı ölçüm kapısı: buraya gelen
 * eşleşme hedef benzersizliğini geçmiş durumda, yani join satırları
 * çoğaltmıyor ve gösterilen sayılar şişmiş değil. Geçemeyen aday zaten
 * kurulmuyor.
 *
 * "Hayır" da kaydediliyor. Yoksa aynı yanlış eşleşme her sorguda yeniden
 * kurulur ve aynı soru tekrar tekrar sorulur.
 */
function MatchConfirmation({ pending, onAnswer }) {
  const [answered, setAnswered] = useState({});
  const [busy, setBusy] = useState(null);

  const open = (pending || []).filter(p => !answered[matchKey(p)]);
  if (!onAnswer || open.length === 0) return null;

  const respond = async (match, accepted) => {
    const key = matchKey(match);
    setBusy(key);
    try {
      await onAnswer(match, accepted);
      setAnswered(prev => ({ ...prev, [key]: accepted ? 'kaydedildi' : 'reddedildi' }));
    } finally {
      setBusy(null);
    }
  };

  return (
    <div className="bi-match-confirm">
      {open.map((match) => {
        const key = matchKey(match);
        const overlap = typeof match.valueOverlap === 'number'
          ? `${Math.round(match.valueOverlap * 100)}%`
          : null;

        return (
          <div className="bi-match-confirm-item" key={key}>
            <div className="bi-match-confirm-title">Bu sonucu bir varsayımla ürettim</div>
            <p className="bi-match-confirm-text">
              Şu iki kolonu eşleştirdim; adları bir harf farklı olduğu için bunu
              tahmin ettim, veritabanı böyle bir bağ bildirmiyor:
            </p>
            <div className="bi-match-confirm-pair">
              <span>{match.fromTable}.{match.fromColumn}</span>
              <span>{match.toTable}.{match.toColumn}</span>
            </div>
            {overlap && (
              <div className="bi-match-confirm-metric">
                Değerlerin <strong>{overlap}</strong>’ı hedef tabloda bulundu.
              </div>
            )}
            <p className="bi-match-confirm-text">
              Yukarıdaki sonuç doğruysa bunu hafızaya yazayım ve bir daha sormayayım.
            </p>
            <div className="bi-match-confirm-actions">
              <button
                type="button"
                className="bi-match-btn bi-match-btn-yes"
                disabled={busy === key}
                onClick={() => respond(match, true)}
              >
                Doğru, hafızaya yaz
              </button>
              <button
                type="button"
                className="bi-match-btn bi-match-btn-no"
                disabled={busy === key}
                onClick={() => respond(match, false)}
              >
                Yanlış, bir daha kurma
              </button>
            </div>
          </div>
        );
      })}
    </div>
  );
}

const matchKey = (m) =>
  `${m.fromTable}.${m.fromColumn}->${m.toTable}.${m.toColumn}`.toLowerCase();

export default function BiChartNode({ data, onRefine, onDelete, onConfirmMatch }) {
  const rootRef = useRef(null);

  // Düzeltme kutusu: yanlış anlaşılmış bir soru için yeni bir grafik
  // üretmek yerine bu grafiğin yerine geçen bir sonuç istenir. Yeni düğüm
  // açmak, tuvalde aynı sorunun iki cevabını yan yana bırakıyordu.
  const [refineOpen, setRefineOpen] = useState(false);

  // Zeminin gerçekten koyu olup olmadığı hesaplanmış arka plandan okunuyor;
  // tema adına güvenmek yetmiyor (bkz. chartTheme.resolveTheme).
  const [theme, setTheme] = useState(() => resolveTheme(null));
  useEffect(() => {
    setTheme(resolveTheme(rootRef.current));
  }, [data]);

  // ── Timeout state (CanvasPage'deki ajan yoklama penceresiyle eşleşir) ──
  const [timedOut, setTimedOut] = useState(false);
  useEffect(() => {
    if (!data?.loading) { setTimedOut(false); return; }
    const t = setTimeout(() => setTimedOut(true), 180_000);
    return () => clearTimeout(t);
  }, [data?.loading]);

  const rawType = data?.type || data?.chartType || 'bar';
  const chartType = normalizeType(rawType);
  const labels = useMemo(() => data?.data?.labels || [], [data]);
  const rawDatasets = data?.data?.datasets;

  const finalDatasets = useMemo(() => {
    // ── Pareto: bar + kümülatif çizgi ──
    if (chartType === 'pareto' && rawDatasets?.length === 1) {
      const vals = rawDatasets[0].data || [];
      const total = vals.reduce((s, v) => s + Number(v), 0) || 1;
      let cum = 0;
      const cumData = vals.map((v) => { cum += Number(v); return +((cum / total) * 100).toFixed(1); });

      return [
        {
          ...buildDatasets(rawDatasets, 'bar', theme)[0],
          type: 'bar',
          yAxisID: 'y',
        },
        {
          label: 'Kümülatif %',
          data: cumData,
          type: 'line',
          borderColor: theme.categorical[1],
          backgroundColor: 'transparent',
          borderWidth: 2,
          pointRadius: 3,
          pointBackgroundColor: theme.categorical[1],
          tension: 0.3,
          yAxisID: 'y2',
        },
      ];
    }

    const built = buildDatasets(rawDatasets, chartType, theme);
    if (built.length) return built;

    // Eski format uyumluluğu: flat values array
    const values = data?.data?.values || [];
    return buildDatasets([{ label: 'Veri', data: values }], chartType, theme);
  }, [chartType, rawDatasets, data, theme]);

  // ── Loading state ──────────────────────────────────────────────────
  if (data?.loading) {
    return (
      <div className="bi-node bi-chart-node" ref={rootRef}>
        <div className="bi-node-header">
          <span className="bi-node-icon">📊</span>
          <span className="bi-node-title">{data?.title || 'Grafik Hazırlanıyor...'}</span>
          <ChartActions onDelete={onDelete} />
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

  const chartData = { labels, datasets: finalDatasets };

  // Tek seride başlık zaten seriyi adlandırıyor; ayrıca bir kutu göstermek
  // yer kaplamaktan başka bir şey yapmıyor.
  const showLegend = finalDatasets.length > 1;

  const tickFont = { size: 11, family: 'Manrope, system-ui, sans-serif' };

  const linearScales = {
    y: {
      beginAtZero: true,
      border: { display: false },
      grid: { color: theme.grid, drawTicks: false },
      ticks: {
        color: theme.muted,
        font: tickFont,
        padding: 8,
        // Binlik ayraçlı Türkçe biçim: 2.500 gibi.
        callback: (v) => (typeof v === 'number' ? v.toLocaleString('tr-TR') : v),
      },
    },
    x: {
      border: { display: false },
      grid: { display: false },
      ticks: {
        color: theme.muted,
        font: tickFont,
        padding: 6,
        autoSkip: true,
        maxRotation: 0,
        callback(value) {
          return truncate(this.getLabelForValue(value));
        },
      },
    },
  };

  const options = {
    responsive: true,
    maintainAspectRatio: false,
    layout: { padding: { top: 4, right: 8, bottom: 0, left: 0 } },
    barPercentage: BAR_PERCENTAGE,
    categoryPercentage: CATEGORY_PERCENTAGE,
    interaction: { mode: 'index', intersect: false },
    plugins: {
      legend: showLegend
        ? {
            display: true,
            position: 'top',
            align: 'end',
            labels: {
              color: theme.muted,
              font: tickFont,
              boxWidth: 10,
              boxHeight: 10,
              usePointStyle: true,
              pointStyle: 'circle',
              padding: 14,
            },
          }
        : { display: false },
      tooltip: {
        backgroundColor: theme.tooltipBg,
        titleColor: theme.tooltipInk,
        bodyColor: theme.tooltipInk,
        padding: 10,
        cornerRadius: 6,
        displayColors: finalDatasets.length > 1,
        // Eksende kısaltılan etiketin tamamı burada görünsün.
        callbacks: {
          title: (items) => (items.length ? String(labels[items[0].dataIndex] ?? '') : ''),
          label: (item) => {
            const v = item.parsed.y ?? item.parsed;
            const num = typeof v === 'number' ? v.toLocaleString('tr-TR') : v;
            return finalDatasets.length > 1 ? `${item.dataset.label}: ${num}` : String(num);
          },
        },
      },
    },
    scales: IDENTITY_TYPES.includes(chartType) || chartType === 'scatter'
      ? undefined
      : chartType === 'pareto'
        ? {
            ...linearScales,
            y: { ...linearScales.y, position: 'left' },
            y2: {
              beginAtZero: true,
              max: 100,
              position: 'right',
              border: { display: false },
              grid: { drawOnChartArea: false },
              ticks: { color: theme.muted, font: tickFont, callback: (v) => `${v}%` },
            },
          }
        : linearScales,
  };

  const renderChart = () => {
    if (chartType === 'pareto' || finalDatasets.some((d) => d.type)) {
      return <Bar data={chartData} options={options} />;
    }
    switch (chartType) {
      case 'bar': return <Bar data={chartData} options={options} />;
      case 'line':
      case 'area': return <Line data={chartData} options={options} />;
      case 'pie': return <Pie data={chartData} options={options} />;
      case 'doughnut':
      case 'donut': return <Doughnut data={chartData} options={options} />;
      case 'radar': return <Radar data={chartData} options={options} />;
      case 'scatter': return (
        <Scatter
          data={chartData}
          options={{ ...options, scales: linearScales }}
        />
      );
      default: return <Bar data={chartData} options={options} />;
    }
  };

  return (
    <div className="bi-node bi-chart-node" ref={rootRef}>
      <div className="bi-node-header">
        <span className="bi-node-icon">📊</span>
        <span className="bi-node-title">{data?.title || 'Grafik'}</span>
        <ChartActions
          refineOpen={refineOpen}
          onToggleRefine={onRefine ? () => setRefineOpen((o) => !o) : undefined}
          onDelete={onDelete}
        />
        <span className="bi-node-type-badge">Chart</span>
      </div>
      <div className="bi-chart-body">
        {renderChart()}
      </div>

      <MatchConfirmation
        pending={data?.pendingConfirmations}
        onAnswer={onConfirmMatch}
      />

      {refineOpen && onRefine && (
        <div className="bi-node-footer">
          <div className="bi-node-footer-hint">
            Yanıt bu grafiğin yerine geçer — yeni bir grafik eklenmez.
          </div>
          <NodeComposer
            placeholder="Ne değişsin? Örn. son 12 ayı çizgi grafik yap"
            autoFocus
            onSubmit={(text) => { setRefineOpen(false); onRefine(text); }}
            onCancel={() => setRefineOpen(false)}
          />
        </div>
      )}
    </div>
  );
}
