import DashboardMock from './DashboardMock';
import { SIGN_UP_URL } from '../config';

const stats = [
  { value: '12 sn', label: 'ortalama dashboard süresi' },
  { value: '40+', label: 'grafik ve widget türü' },
  { value: 'CSV · XLSX · SQL', label: 'desteklenen kaynaklar' },
];

export default function Hero() {
  return (
    <section className="hero">
      <div className="wrap hero-inner">
        <div className="hero-copy">
          <span className="badge">
            <span className="badge-dot" />
            Yapay zekâ destekli veri görselleştirme
          </span>

          <h1 className="hero-title">
            Verini yükle,
            <br />
            gerisini <span className="navy">Grafirio</span> yapsın.
          </h1>

          <p className="hero-lead">
            Excel dosyanı sürükle ya da veri tabanını bağla. Grafirio veriyi okur, yorumlar ve
            saniyeler içinde kanvas üzerinde konuşan bir dashboard kurar.
          </p>

          <div className="hero-cta">
            <a className="btn btn-primary btn-lg" href={SIGN_UP_URL}>
              Ücretsiz başla
              <span className="btn-arrow" aria-hidden="true">
                →
              </span>
            </a>
            <a className="btn btn-secondary btn-lg" href="#nasil-calisir">
              Demoyu izle
            </a>
          </div>

          <dl className="hero-stats">
            {stats.map((s, i) => (
              <div className="hero-stat" key={s.label}>
                {i > 0 && <span className="hero-stat-rule" aria-hidden="true" />}
                <dt>{s.value}</dt>
                <dd>{s.label}</dd>
              </div>
            ))}
          </dl>
        </div>

        <div className="hero-visual">
          <span className="hero-glow" aria-hidden="true" />
          <DashboardMock />
        </div>
      </div>
    </section>
  );
}
