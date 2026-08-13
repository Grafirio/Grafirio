import { useTheme } from '../../contexts/ThemeContext';
import '../../styles/SettingsPages.css';

const ACCENT_SWATCH = {
  blue: 'var(--gf-navy)',
  orange: '#d9691f',
  teal: 'var(--gf-teal)',
  violet: '#6b3a97',
};
const ACCENT_LABEL = {
  blue: 'Lacivert (marka)',
  orange: 'Turuncu',
  teal: 'Teal',
  violet: 'Violet',
};
const DENSITY_LABEL = {
  comfortable: 'Ferah',
  balanced: 'Dengeli',
  compact: 'Yoğun',
};
const DENSITY_NOTE = {
  comfortable: 'geniş nefes payı',
  balanced: 'varsayılan',
  compact: 'daha fazla satır aynı ekranda',
};

/**
 * Ayarlar › Gorunum ve Tema.
 *
 * Tek gercek kontrol paneli burasi: mod, marka rengi, yogunluk. Ucu de
 * ThemeContext uzerinden dogrudan <html>'e yaziliyor ve localStorage'da
 * kaliyor — tasarim taslagindaki gibi dekor degil, calisan bir ozellik.
 *
 * Logo yukleme tasarimda var ama dosya alan bir uc yok; bu yuzden disabled
 * duruyor, tipki dosya yukleme ve sirket profili duzenleme gibi.
 */
export default function ThemeSettingsPage() {
  const { theme, accent, density, setTheme, setAccent, setDensity, ACCENTS, DENSITIES } = useTheme();

  return (
    <div className="st">
      <div className="st-head">
        <div>
          <p className="st-eyebrow">Ayarlar · Görünüm</p>
          <h1>Görünüm ve Tema</h1>
          <p className="st-lead">
            Panel bu değişkenlerden besleniyor; buradaki bir seçim anında ve tüm ekranlarda geçerli
            olur. Tercih tarayıcınızda saklanır.
          </p>
        </div>
      </div>

      <div className="st-grid-2">
        <div className="st-col">
          <section className="st-card">
            <p className="st-caps">Mod</p>
            <div style={{ display: 'grid', gridTemplateColumns: '1fr 1fr', gap: 12 }}>
              <button
                type="button"
                onClick={() => setTheme('light')}
                className="st-theme-swatch"
                data-active={theme === 'light'}
              >
                <span className="st-theme-preview st-theme-preview--light" aria-hidden="true">
                  <span />
                  <span />
                  <span />
                </span>
                <strong>Açık</strong>
                <span>gündüz, ofis ekranı</span>
              </button>
              <button
                type="button"
                onClick={() => setTheme('dark')}
                className="st-theme-swatch"
                data-active={theme === 'dark'}
              >
                <span className="st-theme-preview st-theme-preview--dark" aria-hidden="true">
                  <span />
                  <span />
                  <span />
                </span>
                <strong>Karanlık</strong>
                <span>uzun kanvas oturumları</span>
              </button>
            </div>
          </section>

          <section className="st-card">
            <p className="st-caps">Marka rengi</p>
            <p className="st-card-sub" style={{ marginBottom: 14 }}>
              Yalnızca eylem/vurgu rengi. Grafik paleti ve marka lacivert değişmez — aynı veri her
              firmada aynı renkte görünsün diye.
            </p>
            <div style={{ display: 'flex', gap: 10, flexWrap: 'wrap' }}>
              {ACCENTS.map((a) => (
                <button
                  key={a}
                  type="button"
                  className="st-accent-swatch"
                  data-active={accent === a}
                  onClick={() => setAccent(a)}
                >
                  <span style={{ background: ACCENT_SWATCH[a] }} aria-hidden="true" />
                  {ACCENT_LABEL[a]}
                </button>
              ))}
            </div>
          </section>

          <section className="st-card">
            <p className="st-caps">Yoğunluk</p>
            <div className="st-tabs" role="tablist">
              {DENSITIES.map((d) => (
                <button
                  key={d}
                  type="button"
                  className={density === d ? 'is-active' : ''}
                  onClick={() => setDensity(d)}
                >
                  {DENSITY_LABEL[d]}
                </button>
              ))}
            </div>
            <p className="st-card-sub" style={{ marginTop: 14 }}>
              Şu an seçili: <strong>{DENSITY_LABEL[density]}</strong> — {DENSITY_NOTE[density]}. Kart
              iç boşluğu, tablo satırı ve sayfa kenarı tek değişkenden geliyor.
            </p>
          </section>
        </div>

        <div className="st-col">
          <section className="st-card">
            <p className="st-caps">Logo</p>
            <div style={{ display: 'flex', alignItems: 'center', gap: 16, flexWrap: 'wrap' }}>
              <div
                style={{
                  width: 120,
                  height: 72,
                  border: '1px dashed var(--gf-line)',
                  borderRadius: 12,
                  background: 'var(--gf-paper-warm)',
                  display: 'grid',
                  placeItems: 'center',
                  fontFamily: 'var(--gf-font-mono)',
                  fontSize: 10,
                  color: 'var(--gf-fainter)',
                  textAlign: 'center',
                  lineHeight: 1.5,
                }}
              >
                logo
                <br />
                svg / png
              </div>
              <div style={{ display: 'flex', flexDirection: 'column', gap: 8 }}>
                <button type="button" className="st-btn" disabled>
                  Dosya yükle
                </button>
                <span className="st-mono st-dim" style={{ fontSize: 11 }}>
                  en az 512px · şeffaf zemin
                </span>
              </div>
            </div>
            <p className="st-card-sub" style={{ marginTop: 14 }}>
              Beyaz etiket logosu için dosya yükleme ucu henüz yok.
            </p>
          </section>

          <section className="st-card">
            <p className="st-caps">Grafik paleti · sabit</p>
            <div style={{ display: 'grid', gridTemplateColumns: 'repeat(6, 1fr)', gap: 6 }}>
              {['--gf-c01', '--gf-c02', '--gf-c03', '--gf-c04', '--gf-c05', '--gf-c06',
                '--gf-c07', '--gf-c08', '--gf-c09', '--gf-c10', '--gf-c11', '--gf-c12'].map((c) => (
                <span
                  key={c}
                  style={{ display: 'block', height: 26, borderRadius: 6, background: `var(${c})` }}
                />
              ))}
            </div>
            <p className="st-card-sub" style={{ marginTop: 12 }}>
              Sırayla tüketilir ve marka rengiyle değişmez.
            </p>
          </section>
        </div>
      </div>
    </div>
  );
}
