/**
 * Kart içindeki küçük trend: son N koşu, eskiden yeniye. Çizgi geri planda
 * (soluk), son nokta vurgulu — kartın ana rakamı o noktanın değeri.
 * Etiket ve eksen yok: ayrıntı detay grafiğinde.
 */
export default function Sparkline({ values, domain, width = 120, height = 32, label }) {
  const points = values.filter((v) => Number.isFinite(v));
  if (points.length < 2) {
    return <span className="gf-subtle gf-text-sm">{points.length === 1 ? 'tek koşu' : ''}</span>;
  }

  const [min, max] = domain ?? [Math.min(...points), Math.max(...points)];
  const span = max - min || 1;
  const pad = 5; // son noktanın halkası kesilmesin
  const x = (i) => pad + (i / (points.length - 1)) * (width - pad * 2);
  const y = (v) => pad + (1 - (v - min) / span) * (height - pad * 2);
  const path = points.map((v, i) => `${i === 0 ? 'M' : 'L'}${x(i).toFixed(1)},${y(v).toFixed(1)}`).join(' ');
  const last = points.length - 1;

  return (
    <svg className="sm-spark" width={width} height={height} viewBox={`0 0 ${width} ${height}`} role="img"
      aria-label={label}>
      <path d={path} className="sm-spark__line" />
      <circle cx={x(last)} cy={y(points[last])} r="4" className="sm-spark__dot" />
    </svg>
  );
}
