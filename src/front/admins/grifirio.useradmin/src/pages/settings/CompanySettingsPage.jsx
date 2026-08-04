import { useEffect, useState } from 'react';
import { useKeycloak } from '@react-keycloak/web';
import { describeError, fetchCompanies } from '../../services/companyService';
import '../../styles/SettingsPages.css';

/**
 * Ayarlar › Sirket Ayarlari.
 *
 * Tasarim taslagindaki uc bolum de duruyor: kimlik, bolgesel bicimler ve
 * panel davranisi. Ama Identity tarafinda sirketi guncelleyen bir uc yok
 * (yalnizca olusturma, listeleme ve onboarding var), bolgesel bicim ile
 * panel tercihleri de hicbir yerde saklanmiyor.
 *
 * Bu yuzden alanlar gercek sirket kaydiyla dolduruluyor ama kilitli, ve
 * neden kilitli oldugu sayfada yaziyor. Yazilabilir gorunup "Kaydet"e
 * basildiginda sessizce hicbir sey yapmayan bir form, bos bir sayfadan
 * daha yaniltici olurdu.
 */
export default function CompanySettingsPage() {
  const { keycloak } = useKeycloak();
  const token = keycloak.token;
  const companyId = keycloak.tokenParsed?.company_id;

  const [company, setCompany] = useState(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState('');

  useEffect(() => {
    if (!companyId) {
      setLoading(false);
      return undefined;
    }
    let cancelled = false;
    (async () => {
      try {
        const list = await fetchCompanies(token);
        if (!cancelled) setCompany(list.find((c) => c.id === companyId) || null);
      } catch (err) {
        if (!cancelled) setError(describeError(err, 'Şirket bilgileri okunamadı.'));
      } finally {
        if (!cancelled) setLoading(false);
      }
    })();
    return () => {
      cancelled = true;
    };
  }, [token, companyId]);

  return (
    <div className="st">
      <div className="st-head">
        <div>
          <p className="st-eyebrow">Ayarlar · Şirket</p>
          <h1>Şirket Ayarları</h1>
          <p className="st-lead">
            Kurum kimliği, bölgesel biçimler ve panel varsayılanları. Değişiklikler tüm alt
            şirketlere uygulanır.
          </p>
        </div>
      </div>

      {error && <div className="st-alert">{error}</div>}
      {loading && <div className="st-card st-empty">Yükleniyor…</div>}

      {!loading && !companyId && (
        <div className="st-card">
          <p className="st-empty">
            Hesabınız henüz bir şirkete bağlı değil. Çalışma alanınızı kurduktan sonra bu sayfa
            şirketinizin ayarlarını gösterir.
          </p>
        </div>
      )}

      {!loading && companyId && (
        <>
          <div className="st-grid-2">
            <section className="st-card">
              <p className="st-caps">Kimlik</p>

              <div className="st-form-grid" style={{ gridTemplateColumns: '1fr' }}>
                <label className="st-field">
                  <span>Şirket adı</span>
                  <input value={company?.name ?? ''} readOnly disabled />
                </label>
              </div>

              <div className="st-form-grid" style={{ marginTop: 14 }}>
                <label className="st-field st-field--mono">
                  <span>Kısa kod</span>
                  <input value={company?.code ?? '—'} readOnly disabled />
                </label>
                <label className="st-field st-field--mono">
                  <span>Hiyerarşi seviyesi</span>
                  <input value={company?.level ?? 0} readOnly disabled />
                </label>
              </div>

              <div className="st-form-grid" style={{ gridTemplateColumns: '1fr', marginTop: 14 }}>
                <label className="st-field">
                  <span>Açıklama</span>
                  <textarea rows="3" value={company?.description ?? ''} readOnly disabled />
                </label>
              </div>
            </section>

            <div className="st-col">
              <section className="st-card">
                <p className="st-caps">Bölgesel biçimler</p>
                <div className="st-form-grid">
                  <label className="st-field">
                    <span>Dil</span>
                    <select disabled>
                      <option>Türkçe</option>
                    </select>
                  </label>
                  <label className="st-field">
                    <span>Saat dilimi</span>
                    <select disabled>
                      <option>(UTC+03:00) İstanbul</option>
                    </select>
                  </label>
                  <label className="st-field">
                    <span>Para birimi</span>
                    <select disabled>
                      <option>₺ Türk lirası</option>
                    </select>
                  </label>
                  <label className="st-field">
                    <span>Tarih biçimi</span>
                    <select disabled>
                      <option>GG.AA.YYYY</option>
                    </select>
                  </label>
                </div>
                <p className="st-card-sub" style={{ marginTop: 14 }}>
                  Panel şu an bu biçimleri sabit kullanıyor; seçim yapılabilmesi için tercihleri
                  saklayacak bir uç gerekiyor.
                </p>
              </section>

              <section className="st-card">
                <p className="st-caps">Panel davranışı</p>
                <div className="st-switch">
                  <span className="st-switch-text">
                    <strong>Otomatik Grafirio yorumu</strong>
                    <span>Her yeni kanvasta özet üret</span>
                  </span>
                  <span className="st-switch-knob is-on" aria-hidden="true" />
                </div>
                <div className="st-switch">
                  <span className="st-switch-text">
                    <strong>Haftalık e-posta özeti</strong>
                    <span>Pazartesi 09:00 · yetkili kullanıcılara</span>
                  </span>
                  <span className="st-switch-knob" aria-hidden="true" />
                </div>
                <div className="st-switch">
                  <span className="st-switch-text">
                    <strong>Alt şirket verilerini birleştir</strong>
                    <span>Kapalıyken her şirket ayrı raporlanır</span>
                  </span>
                  <span className="st-switch-knob" aria-hidden="true" />
                </div>
              </section>
            </div>
          </div>

          <div className="st-note">
            <i>i</i>
            <div>
              Bu sayfadaki alanlar şu an <strong>salt okunur</strong>. Şirket kaydını güncelleyen
              bir Identity ucu (<span className="st-mono">PUT /v1/identity/companies</span>) henüz
              yok; bölgesel biçim ve panel tercihleri de hiçbir yerde saklanmıyor. Uçlar
              yazıldığında aynı form yazılabilir hale gelecek.
            </div>
          </div>

          <div className="st-savebar">
            <span className="st-savebar-note">düzenleme kapalı · sunucu ucu bekleniyor</span>
            <span className="st-savebar-actions">
              <button type="button" className="st-btn st-btn--ghost" disabled>
                Vazgeç
              </button>
              <button type="button" className="st-btn" disabled>
                Kaydet
              </button>
            </span>
          </div>
        </>
      )}
    </div>
  );
}
