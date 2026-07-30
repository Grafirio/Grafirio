import GMark from './GMark';

// Urunun ekran goruntusu yerine cizilmis temsili. Gercek ekran goruntusu her
// arayuz degisikliginde bayatliyor; burasi "neye benziyor" sorusuna cevap
// verdigi icin cizim yeterli ve her yogunlukta keskin duruyor.

const revenue = [38, 52, 44, 68, 59, 88, 74];
const REVENUE_ACCENT = { 3: 'is-navy', 5: 'is-teal' };

const regions = [
  { name: 'Ege', pct: 86, color: '#0E8F8C' },
  { name: 'Marmara', pct: 72, color: '#2F5FA8' },
  { name: 'İç Anadolu', pct: 48, color: '#8A2E8E' },
  { name: 'Akdeniz', pct: 34, color: '#F0902B' },
];

const channels = [
  { name: 'Bayi', pct: 38, color: '#0E8F8C' },
  { name: 'E-ticaret', pct: 25, color: '#F8C630' },
  { name: 'Kurumsal', pct: 19, color: '#8A2E8E' },
  { name: 'Diğer', pct: 18, color: '#E4633C' },
];

// conic-gradient duraklarini yuzdelerden kur; elle yazilinca bir dilim
// degistiginde geri kalani kaymis oluyordu.
const conic = (() => {
  let at = 0;
  const stops = channels.map((c) => {
    const from = at;
    at += c.pct;
    return `${c.color} ${from}% ${at}%`;
  });
  return `conic-gradient(${stops.join(', ')})`;
})();

export default function DashboardMock() {
  return (
    <div className="mock">
      <div className="mock-chrome">
        <span className="mock-dot" style={{ background: '#E4633C' }} />
        <span className="mock-dot" style={{ background: '#F8C630' }} />
        <span className="mock-dot" style={{ background: '#0E8F8C' }} />
        <span className="mono mock-file">satis_2026_q2.xlsx · kanvas</span>
      </div>

      <div className="mock-body">
        <div className="mock-note">
          <span className="mock-note-mark">
            <GMark compact barColor="#FBFAF8" />
          </span>
          <div>
            <div className="mono mock-note-label">Grafirio yorumu</div>
            <p>
              Mayıs’ta Ege bölgesi cirosu %28 arttı; büyümenin %71’i tek bir ürün grubundan
              geliyor. Stok devir hızını kontrol etmenizi öneririm.
            </p>
          </div>
        </div>

        <div className="mock-card">
          <span className="mock-card-label">Aylık ciro</span>
          <strong className="mock-figure">₺4,82M</strong>
          <div className="mock-bars">
            {revenue.map((h, i) => (
              <span
                key={i}
                className={`mock-bar ${REVENUE_ACCENT[i] || ''}`}
                style={{ '--h': `${h}%`, '--d': `${i * 70}ms` }}
              />
            ))}
          </div>
        </div>

        <div className="mock-card">
          <span className="mock-card-label">Kanal dağılımı</span>
          <div className="mock-donut-row">
            <span className="mock-donut" style={{ background: conic }} />
            <ul className="mock-legend">
              {channels.map((c) => (
                <li key={c.name}>
                  <i style={{ background: c.color }} />
                  {c.name} %{c.pct}
                </li>
              ))}
            </ul>
          </div>
        </div>

        <div className="mock-card mock-card-wide">
          <div className="mock-card-head">
            <span className="mock-card-label">Bölge kırılımı</span>
            <span className="mono mock-auto">otomatik oluşturuldu</span>
          </div>
          <ul className="mock-regions">
            {regions.map((r) => (
              <li key={r.name}>
                <span className="mock-region-name">{r.name}</span>
                <span className="mock-track">
                  <span
                    className="mock-fill"
                    style={{ '--w': `${r.pct}%`, background: r.color }}
                  />
                </span>
              </li>
            ))}
          </ul>
        </div>
      </div>
    </div>
  );
}
