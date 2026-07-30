// Marka isareti. Alti radyal bar, farkli uzunlukta — halka degil, veri okumasi.
// 64 px altinda "compact" devreye giriyor: bes esit dilim, daha genis agiz ve
// daha kalin G govdesi. Kucukte alti dilimin kesme bosluklari kapaniyor ve
// isaret lekeye donuyordu.
//
// Dilim renkleri ve uzunluklari marka kilavuzunda "degistirilmez" olarak
// isaretli; prop olarak disari acilan tek sey G'nin rengi (koyu zeminde acik
// renge doner).

const FULL_SLICES = [
  { d: 'M149.99 154.56A74 74 0 0 1 106.45 173.72L104.53 151.80A52 52 0 0 0 135.13 138.34Z', fill: '#F0902B' },
  { d: 'M100.00 190.00A90 90 0 0 1 45.21 171.40L68.34 141.25A52 52 0 0 0 100.00 152.00Z', fill: '#F8C630' },
  { d: 'M45.95 158.98A80 80 0 0 1 21.22 113.89L48.79 109.03A52 52 0 0 0 64.87 138.34Z', fill: '#F5B32A' },
  { d: 'M6.36 108.19A94 94 0 0 1 20.72 49.49L56.14 72.06A52 52 0 0 0 48.20 104.53Z', fill: '#E4633C' },
  { d: 'M38.12 52.52A78 78 0 0 1 79.81 24.66L86.54 49.77A52 52 0 0 0 58.75 68.34Z', fill: '#C42E6E' },
  { d: 'M84.72 13.34A88 88 0 0 1 140.63 21.94L124.01 53.88A52 52 0 0 0 90.97 48.79Z', fill: '#0E8F8C' },
];

const FULL_BARS = [
  { x: 100, y: 88, width: 62, height: 24, rx: 2 },
  { x: 140, y: 88, width: 24, height: 46, rx: 2 },
];

const COMPACT_SLICES = [
  { d: 'M162.74 167.28A92 92 0 0 1 93.58 191.78L96.09 155.86A56 56 0 0 0 138.19 140.96Z', fill: '#F0902B' },
  { d: 'M87.20 191.10A92 92 0 0 1 24.64 152.77L54.13 132.12A56 56 0 0 0 92.21 155.46Z', fill: '#F8C630' },
  { d: 'M21.14 147.38A92 92 0 0 1 11.56 74.64L46.17 84.56A56 56 0 0 0 52.00 128.84Z', fill: '#F5B32A' },
  { d: 'M13.55 68.53A92 92 0 0 1 64.05 15.31L78.12 48.45A56 56 0 0 0 47.38 80.85Z', fill: '#E4633C' },
  { d: 'M70.05 13.01A92 92 0 0 1 143.19 18.77L126.29 50.55A56 56 0 0 0 81.77 47.05Z', fill: '#0E8F8C' },
];

const COMPACT_BARS = [
  { x: 96, y: 84, width: 76, height: 32, rx: 3 },
  { x: 140, y: 84, width: 32, height: 54, rx: 3 },
];

export default function GMark({ compact = false, barColor = '#1C3F7C', className = '', title }) {
  const slices = compact ? COMPACT_SLICES : FULL_SLICES;
  const bars = compact ? COMPACT_BARS : FULL_BARS;

  return (
    <svg
      className={`gmark ${className}`}
      viewBox="0 0 200 200"
      role={title ? 'img' : 'presentation'}
      aria-label={title}
      aria-hidden={title ? undefined : true}
    >
      {slices.map((s) => (
        <path key={s.d} d={s.d} fill={s.fill} />
      ))}
      {bars.map((b) => (
        <rect key={`${b.x}-${b.y}-${b.width}`} {...b} fill={barColor} />
      ))}
    </svg>
  );
}
