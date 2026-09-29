import { useMemo, useRef, useState } from 'react';
import { formatValue } from '../../services/semanticApi';

const WIDTH = 720;
const HEIGHT = 220;
const MARGIN = { top: 12, right: 16, bottom: 28, left: 48 };

/** Temiz eksen adımları: oranlarda 0–100%, diğerlerinde 4 eşit yuvarlak adım. */
function ticks(min, max, unit) {
  if (unit === 'ratio') return [0, 0.25, 0.5, 0.75, 1];
  const span = max - min || Math.abs(max) || 1;
  const raw = span / 4;
  const magnitude = 10 ** Math.floor(Math.log10(raw));
  const step = [1, 2, 2.5, 5, 10].map((m) => m * magnitude).find((s) => s >= raw) ?? raw;
  const start = Math.floor(min / step) * step;
  const result = [];
  for (let v = start; v <= max + step / 2; v += step) result.push(Number(v.toFixed(10)));
  return result;
}

const shortDate = (iso) =>
  new Date(iso).toLocaleDateString('tr-TR', { day: '2-digit', month: '2-digit' });

/**
 * Bir metriğin koşular boyunca seyri — tek seri, tek eksen. Fare hangi koşuya
 * yakınsa onun tarihi, etiketi ve commit'i ipucunda görünür; tıklayınca o koşu
 * seçilir. Altındaki tablo aynı verinin erişilebilir hâli.
 */
export default function TrendChart({ points, unit, onSelect, selectedId, label }) {
  const [hover, setHover] = useState(null);
  const svgRef = useRef(null);

  const layout = useMemo(() => {
    const values = points.map((p) => p.value).filter(Number.isFinite);
    if (values.length === 0) return null;
    const min = unit === 'ratio' ? 0 : Math.min(0, ...values);
    const max = unit === 'ratio' ? 1 : Math.max(...values) * 1.1 || 1;
    const axis = ticks(min, max, unit);
    const lo = Math.min(min, axis[0]);
    const hi = Math.max(max, axis[axis.length - 1]);
    const innerW = WIDTH - MARGIN.left - MARGIN.right;
    const innerH = HEIGHT - MARGIN.top - MARGIN.bottom;
    const x = (i) => MARGIN.left + (points.length === 1 ? innerW / 2 : (i / (points.length - 1)) * innerW);
    const y = (v) => MARGIN.top + (1 - (v - lo) / (hi - lo || 1)) * innerH;
    return { axis, x, y };
  }, [points, unit]);

  if (!layout) return <div className="gf-empty"><p>Bu metrik için ölçüm yok.</p></div>;
  const { axis, x, y } = layout;

  const path = points
    .map((p, i) => (Number.isFinite(p.value) ? `${i === 0 ? 'M' : 'L'}${x(i).toFixed(1)},${y(p.value).toFixed(1)}` : ''))
    .join(' ');

  // Fare en yakın koşuya yapışıyor: küçük noktaları tam hedeflemek gerekmesin.
  const onMove = (event) => {
    const rect = svgRef.current.getBoundingClientRect();
    const px = ((event.clientX - rect.left) / rect.width) * WIDTH;
    let nearest = 0;
    points.forEach((_, i) => { if (Math.abs(x(i) - px) < Math.abs(x(nearest) - px)) nearest = i; });
    setHover(nearest);
  };

  const hovered = hover === null ? null : points[hover];
  const labelEvery = Math.max(1, Math.ceil(points.length / 8));

  return (
    <div className="sm-trend">
      <svg ref={svgRef} viewBox={`0 0 ${WIDTH} ${HEIGHT}`} className="sm-trend__svg" role="img" aria-label={label}
        onMouseMove={onMove} onMouseLeave={() => setHover(null)}
        onClick={() => hovered && onSelect?.(hovered.id)}>
        {axis.map((tick) => (
          <g key={tick}>
            <line x1={MARGIN.left} x2={WIDTH - MARGIN.right} y1={y(tick)} y2={y(tick)} className="sm-trend__grid" />
            <text x={MARGIN.left - 8} y={y(tick)} className="sm-trend__tick" textAnchor="end" dominantBaseline="middle">
              {formatValue(tick, unit)}
            </text>
          </g>
        ))}
        {points.map((p, i) => (i % labelEvery === 0 || i === points.length - 1) && (
          <text key={p.id} x={x(i)} y={HEIGHT - 8} className="sm-trend__tick" textAnchor="middle">
            {shortDate(p.startedAt)}
          </text>
        ))}

        {hovered && <line x1={x(hover)} x2={x(hover)} y1={MARGIN.top} y2={HEIGHT - MARGIN.bottom} className="sm-trend__cross" />}
        <path d={path} className="sm-trend__line" />
        {points.map((p, i) => Number.isFinite(p.value) && (
          <circle key={p.id} cx={x(i)} cy={y(p.value)} r={p.id === selectedId || i === hover ? 5 : 4}
            className={`sm-trend__dot${p.id === selectedId ? ' is-selected' : ''}`} />
        ))}
      </svg>

      {hovered && (
        // Kenarlarda ipucu noktanin ic tarafina hizalaniyor; ortada ortalaniyor.
        // Yoksa son koşunun ipucu grafigin (ve sayfanin) disina tasiyordu.
        <div className="sm-trend__tip" style={{
          left: `${(x(hover) / WIDTH) * 100}%`,
          transform: `translateX(${x(hover) / WIDTH > 0.75 ? '-100%' : x(hover) / WIDTH < 0.25 ? '0' : '-50%'})`,
        }}>
          <strong>{formatValue(hovered.value, unit)}</strong>
          <span>{new Date(hovered.startedAt).toLocaleString('tr-TR')}</span>
          {hovered.label && <span>{hovered.label}</span>}
          {hovered.gitCommit && <span className="pa-mono">{hovered.gitBranch ?? ''}@{hovered.gitCommit}</span>}
          <span className="gf-subtle">Tıklayınca bu koşu seçilir</span>
        </div>
      )}
    </div>
  );
}
