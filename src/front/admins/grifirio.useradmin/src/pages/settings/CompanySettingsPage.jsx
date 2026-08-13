import { useCallback, useEffect, useState } from 'react';
import { useKeycloak } from '@react-keycloak/web';
import { createSubCompany, describeError, fetchCompanies } from '../../services/companyService';
import '../../styles/SettingsPages.css';

/**
 * Ayarlar › Sirket Ayarlari.
 *
 * Onceden "Sirket profili" ve "Alt sirketler" ayri bir menu basligi altinda
 * (CompanyAdminPage, /company-info) yasiyordu, "Yetkili kullanicilar" da
 * orada uctu bir sekmeydi. Yetkili kullanicilar artik Ayarlar > Kullanicilar
 * sayfasinda (kim ne yapabilir sorusuna orada bakiliyor); burada yalnizca
 * sirketin kendi kimligi ve hiyerarsisi kaliyor.
 *
 * Kimlik/bolgesel/panel-davranisi alanlari salt-okunur: Identity'de sirketi
 * guncelleyen bir uc yok (yalnizca olusturma, listeleme ve onboarding var).
 * Alt sirket olusturma ise gercek, calisan bir uc (createSubCompany) — o
 * yuzden yalnizca o form etkilesimli.
 */
export default function CompanySettingsPage() {
  const { keycloak } = useKeycloak();
  const token = keycloak.token;
  const companyId = keycloak.tokenParsed?.company_id;

  const [tab, setTab] = useState('info');
  const [companies, setCompanies] = useState([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState('');
  const [notice, setNotice] = useState('');

  const load = useCallback(async () => {
    if (!companyId) {
      setLoading(false);
      return;
    }
    setLoading(true);
    setError('');
    try {
      const list = await fetchCompanies(token);
      setCompanies(list);
    } catch (err) {
      setError(describeError(err, 'Şirket bilgileri okunamadı.'));
    } finally {
      setLoading(false);
    }
  }, [token, companyId]);

  useEffect(() => {
    load();
  }, [load]);

  const company = companies.find((c) => c.id === companyId) || null;
  const children = companies.filter((c) => c.parentCompanyId === companyId);

  return (
    <div className="st">
      <div className="st-head">
        <div>
          <p className="st-eyebrow">Ayarlar · Şirket</p>
          <h1>Şirket Ayarları</h1>
          <p className="st-lead">
            Kurum kimliği, bölgesel biçimler, panel varsayılanları ve alt şirket hiyerarşisi.
          </p>
        </div>
      </div>

      {!loading && companyId && (
        <div className="st-tabs" role="tablist">
          <button type="button" className={tab === 'info' ? 'is-active' : ''} onClick={() => setTab('info')}>
            Şirket bilgileri
          </button>
          <button type="button" className={tab === 'tree' ? 'is-active' : ''} onClick={() => setTab('tree')}>
            Alt şirketler {children.length > 0 && `(${children.length})`}
          </button>
        </div>
      )}

      {error && <div className="st-alert">{error}</div>}
      {notice && <div className="st-ok">{notice}</div>}
      {loading && <div className="st-card st-empty">Yükleniyor…</div>}

      {!loading && !companyId && (
        <div className="st-card">
          <p className="st-empty">
            Hesabınız henüz bir şirkete bağlı değil. Çalışma alanınızı kurduktan sonra bu sayfa
            şirketinizin ayarlarını gösterir.
          </p>
        </div>
      )}

      {!loading && companyId && tab === 'info' && (
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

      {!loading && companyId && tab === 'tree' && (
        <SubCompaniesPanel
          token={token}
          parentId={companyId}
          children={children}
          onCreated={load}
          setError={setError}
          setNotice={setNotice}
        />
      )}
    </div>
  );
}

function SubCompaniesPanel({ token, parentId, children, onCreated, setError, setNotice }) {
  const [form, setForm] = useState({ name: '', code: '' });
  const [busy, setBusy] = useState(false);

  const submit = async (e) => {
    e.preventDefault();
    if (!form.name.trim()) {
      setError('Alt şirket adı gerekli.');
      return;
    }
    setBusy(true);
    setError('');
    setNotice('');
    try {
      await createSubCompany(token, {
        name: form.name.trim(),
        code: form.code.trim(),
        parentCompanyId: parentId,
      });
      setForm({ name: '', code: '' });
      setNotice('Alt şirket oluşturuldu.');
      await onCreated();
    } catch (err) {
      setError(describeError(err, 'Alt şirket oluşturulamadı.'));
    } finally {
      setBusy(false);
    }
  };

  return (
    <section className="st-card">
      {children.length === 0 ? (
        <p className="st-empty">Henüz alt şirket yok.</p>
      ) : (
        <div className="st-table-wrap">
          <table className="st-table">
            <thead>
              <tr>
                <th>Ad</th>
                <th>Kısa kod</th>
                <th>Seviye</th>
              </tr>
            </thead>
            <tbody>
              {children.map((c) => (
                <tr key={c.id}>
                  <td className="st-strong">{c.name}</td>
                  <td className="st-mono st-dim">{c.code || '—'}</td>
                  <td className="st-mono st-dim">{c.level}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}

      <form onSubmit={submit} style={{ marginTop: 20, display: 'flex', flexDirection: 'column', gap: 14 }}>
        <p className="st-caps" style={{ margin: 0 }}>Alt şirket ekle</p>
        <div className="st-form-grid">
          <label className="st-field">
            <span>Ad</span>
            <input
              value={form.name}
              onChange={(e) => setForm({ ...form, name: e.target.value })}
              placeholder="Akdeniz Tekstil Lojistik"
            />
          </label>
          <label className="st-field st-field--mono">
            <span>Kısa kod</span>
            <input
              value={form.code}
              onChange={(e) => setForm({ ...form, code: e.target.value })}
              placeholder="AKD-LOJ"
            />
          </label>
        </div>
        <button type="submit" className="st-btn" disabled={busy} style={{ alignSelf: 'flex-start' }}>
          {busy ? 'Ekleniyor…' : '+ Alt şirket ekle'}
        </button>
      </form>
    </section>
  );
}
