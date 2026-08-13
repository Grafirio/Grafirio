import { useState } from 'react';
import '../../styles/SettingsPages.css';

const EVENTS = [
  { name: 'Haftalık özet', desc: 'Pazartesi 09:00 — aktif analizlerin özeti' },
  { name: 'Analiz tamamlandı', desc: 'Uzun süren bir analiz bittiğinde' },
  { name: 'Bağlantı hatası', desc: 'Veri kaynağına erişilemediğinde' },
  { name: 'Yeni kullanıcı', desc: 'Panele bir kullanıcı eklendiğinde' },
  { name: 'Üyelik uyarısı', desc: 'Deneme süresi ve limit aşımı hatırlatmaları' },
];

/**
 * Ayarlar › Bildirimler.
 *
 * Tamamen taslak: e-posta gonderen bir servis yok, bu yuzden anahtarlar
 * tarayicida tutuluyor ve hicbir yere kaydedilmiyor — sayfadan cikildiginda
 * sifirlanir. DataEntryPage/CompanySettingsPage'deki ".st-note" deseniyle
 * ayni durustluk: ne calisiyor ne calismiyor acikca yaziyor.
 */
export default function NotificationsPage() {
  const [grid, setGrid] = useState(() => EVENTS.map(() => ({ mail: true, app: true })));

  const toggle = (i, key) =>
    setGrid((g) => g.map((row, ri) => (ri === i ? { ...row, [key]: !row[key] } : row)));

  return (
    <div className="st">
      <div className="st-head">
        <div>
          <p className="st-eyebrow">Ayarlar · Bildirimler</p>
          <h1>Bildirimler</h1>
          <p className="st-lead">
            Hangi olayın hangi kanaldan gideceğini burada belirlersiniz. Şirket genelinde geçerli
            olacak şekilde tasarlandı.
          </p>
        </div>
      </div>

      <div className="st-grid-2">
        <section className="st-card">
          <div className="st-table-wrap">
            <table className="st-table">
              <thead>
                <tr>
                  <th>Olay</th>
                  <th style={{ textAlign: 'center', width: 100 }}>E-posta</th>
                  <th style={{ textAlign: 'center', width: 100 }}>Panel içi</th>
                </tr>
              </thead>
              <tbody>
                {EVENTS.map((e, i) => (
                  <tr key={e.name}>
                    <td>
                      <strong className="st-strong" style={{ display: 'block' }}>{e.name}</strong>
                      <span style={{ fontSize: 12.5, color: 'var(--gf-muted)' }}>{e.desc}</span>
                    </td>
                    <td style={{ textAlign: 'center' }}>
                      <button
                        type="button"
                        className="st-switch-knob-btn"
                        data-on={grid[i].mail}
                        onClick={() => toggle(i, 'mail')}
                        aria-pressed={grid[i].mail}
                        aria-label={`${e.name} — e-posta`}
                      >
                        <i />
                      </button>
                    </td>
                    <td style={{ textAlign: 'center' }}>
                      <button
                        type="button"
                        className="st-switch-knob-btn"
                        data-on={grid[i].app}
                        onClick={() => toggle(i, 'app')}
                        aria-pressed={grid[i].app}
                        aria-label={`${e.name} — panel içi`}
                      >
                        <i />
                      </button>
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        </section>

        <aside className="st-col">
          <section className="st-card">
            <p className="st-caps">Ek alıcılar</p>
            <p className="st-card-sub" style={{ marginBottom: 12 }}>
              Panelde kullanıcısı olmayan kişiler de özet alabilir; bu adresler analiz içeriğine
              erişmez.
            </p>
            <label className="st-field">
              <input placeholder="ad@sirket.com" disabled />
            </label>
          </section>
          <section className="st-card" style={{ background: 'var(--gf-paper-warm)', borderStyle: 'dashed' }}>
            <p className="st-caps">Gönderim servisi</p>
            <p className="st-card-sub">
              E-posta ucu henüz bağlanmadı. Yukarıdaki anahtarlar bu oturumda kalıyor; servis
              devreye girdiğinde kalıcı hale gelecek.
            </p>
          </section>
        </aside>
      </div>
    </div>
  );
}
