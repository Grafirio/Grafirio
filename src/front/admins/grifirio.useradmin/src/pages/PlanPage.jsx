import { useEffect, useState } from 'react';
import { useNavigate } from 'react-router-dom';
import { useKeycloak } from '@react-keycloak/web';
import { PLAN_NAMES, describeError, fetchSubscriptions } from '../services/planService';
import '../styles/SettingsPages.css';
import '../styles/PlanPage.css';

const fmtDate = (iso) => (iso ? new Date(iso).toLocaleDateString('tr-TR') : '—');
const fmtMoney = (n) =>
  new Intl.NumberFormat('tr-TR', { style: 'currency', currency: 'TRY', maximumFractionDigits: 2 }).format(n);

// Taslakta "Bu donem kullanim" barlari gercek yuzdelerle doluydu (analiz
// sayisi, depolama...). Olcum servisi yok — bar bos/gri duruyor, yuzde
// yerine "servis bekleniyor" yaziyor. Sayi uydurmaktansa boyle.
const USAGE_ROWS = [
  { label: 'Analiz sayısı' },
  { label: 'Kanvas sorgusu' },
  { label: 'Depolama' },
];

export default function PlanPage() {
  const navigate = useNavigate();
  const { keycloak } = useKeycloak();
  const token = keycloak.token;
  const companyId = keycloak.tokenParsed?.company_id;

  const [subs, setSubs] = useState([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState('');

  useEffect(() => {
    if (!companyId) {
      setLoading(false);
      return;
    }
    let cancelled = false;
    (async () => {
      try {
        const list = await fetchSubscriptions(token, companyId);
        if (!cancelled) setSubs(list);
      } catch (err) {
        if (!cancelled) setError(describeError(err, 'Abonelik bilgisi okunamadı.'));
      } finally {
        if (!cancelled) setLoading(false);
      }
    })();
    return () => {
      cancelled = true;
    };
  }, [token, companyId]);

  const current = subs.find((s) => s.isCurrentlyActive) || null;
  const trialEndsAt = current?.trialEndsAt ? new Date(current.trialEndsAt) : null;
  const inTrial = trialEndsAt && trialEndsAt > new Date();
  const trialDaysLeft = inTrial
    ? Math.max(0, Math.ceil((trialEndsAt - new Date()) / 86400000))
    : null;

  return (
    <div className="st">
      <div className="pl-head">
        <div>
          <p className="pl-eyebrow">Ayarlar · Abonelik</p>
          <h1>Üyelik</h1>
          <p className="pl-lead">Planınız, kullanımınız ve abonelik geçmişi.</p>
        </div>
        <div className="pl-head-actions">
          <button type="button" className="st-btn st-btn--ghost" onClick={() => navigate('/onboarding')}>
            Kurulum akışını gör →
          </button>
        </div>
      </div>

      {error && <div className="pl-alert">{error}</div>}
      {loading && <div className="pl-card pl-loading">Yükleniyor…</div>}

      {!loading && !current && !error && (
        <div className="pl-card pl-empty">
          <h2>Aktif aboneliğiniz yok</h2>
          <p>Firmanız için henüz bir abonelik başlatılmamış ya da süresi dolmuş.</p>
        </div>
      )}

      {current && (
        <div className="pl-grid">
          <div className="pl-plan-card">
            <p className="pl-plan-eyebrow">Mevcut plan</p>
            <div className="pl-plan-name">{PLAN_NAMES[current.plan] || current.plan}</div>

            {inTrial && (
              <div className="pl-trial">
                Ücretsiz deneme · {trialDaysLeft} gün kaldı ({fmtDate(current.trialEndsAt)} tarihine kadar)
              </div>
            )}

            {current.plan === 'CREDIT' && (
              <div className="pl-credit">
                Kalan bakiye
                <strong>{fmtMoney(current.creditBalance ?? 0)}</strong>
              </div>
            )}

            <div className="pl-rule" />

            <dl className="pl-facts">
              <div>
                <dt>Durum</dt>
                <dd>
                  <span className={`pl-badge pl-badge--${current.status.toLowerCase()}`}>
                    {current.status === 'ACTIVE' ? 'Aktif' : current.status}
                  </span>
                </dd>
              </div>
              <div>
                <dt>Başlangıç</dt>
                <dd>{fmtDate(current.startsAt)}</dd>
              </div>
              <div>
                <dt>Bitiş</dt>
                <dd>{current.endsAt ? fmtDate(current.endsAt) : 'Süresiz'}</dd>
              </div>
            </dl>

            <button type="button" className="pl-plan-upgrade" disabled title="Ödeme sağlayıcısı henüz bağlanmadı">
              Planı yükselt
            </button>
          </div>

          <div className="pl-col">
            <section className="pl-card">
              <h2>Bu dönem kullanım</h2>
              {USAGE_ROWS.map((u) => (
                <div className="pl-usage-row" key={u.label}>
                  <div className="pl-usage-row-head">
                    <span>{u.label}</span>
                    <span>servis bekleniyor</span>
                  </div>
                  <span className="pl-usage-bar">
                    <span className="pl-usage-fill" style={{ width: 0 }} />
                  </span>
                </div>
              ))}
              <p className="pl-note" style={{ marginTop: 16 }}>
                Limitler taslak: ölçüm servisi bağlanınca gerçek sayılarla dolacak.
              </p>
            </section>

            <section className="pl-card">
              <h2>Geçmiş</h2>
              <table className="pl-table">
                <thead>
                  <tr>
                    <th>Plan</th>
                    <th>Başlangıç</th>
                    <th>Bitiş</th>
                    <th>Durum</th>
                  </tr>
                </thead>
                <tbody>
                  {subs.length === 0 ? (
                    <tr className="st-table-empty">
                      <td colSpan={4}>Kayıtlı abonelik yok.</td>
                    </tr>
                  ) : (
                    subs.map((s) => (
                      <tr key={s.id}>
                        <td>{PLAN_NAMES[s.plan] || s.plan}</td>
                        <td>{fmtDate(s.startsAt)}</td>
                        <td>{s.endsAt ? fmtDate(s.endsAt) : 'Süresiz'}</td>
                        <td>{s.status === 'ACTIVE' ? 'Aktif' : s.status === 'CANCELLED' ? 'İptal edildi' : 'Süresi doldu'}</td>
                      </tr>
                    ))
                  )}
                </tbody>
              </table>
            </section>
          </div>
        </div>
      )}

      <p className="pl-footnote">
        Kullanım limitleri, fatura geçmişi ve ödeme yöntemi burada henüz gösterilmiyor —
        bunlar için satın alma sağlayıcısının (iyzico vb.) bağlanması gerekiyor.
      </p>
    </div>
  );
}
