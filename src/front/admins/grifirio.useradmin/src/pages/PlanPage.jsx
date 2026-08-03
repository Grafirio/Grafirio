import { useEffect, useState } from 'react';
import { useKeycloak } from '@react-keycloak/web';
import { PLAN_NAMES, describeError, fetchSubscriptions } from '../services/planService';
import '../styles/PlanPage.css';

const fmtDate = (iso) => (iso ? new Date(iso).toLocaleDateString('tr-TR') : '—');
const fmtMoney = (n) =>
  new Intl.NumberFormat('tr-TR', { style: 'currency', currency: 'TRY', maximumFractionDigits: 2 }).format(n);

export default function PlanPage() {
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
    <div className="pl">
      <div className="pl-head">
        <p className="pl-eyebrow">Ayarlar · Abonelik</p>
        <h1>Üyelik Bilgileri</h1>
        <p className="pl-lead">Planınız ve abonelik durumunuz.</p>
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
          </div>

          <div className="pl-card">
            <h2>Geçmiş</h2>
            {subs.length <= 1 ? (
              <p className="pl-note">Başka kayıtlı abonelik yok.</p>
            ) : (
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
                  {subs.map((s) => (
                    <tr key={s.id}>
                      <td>{PLAN_NAMES[s.plan] || s.plan}</td>
                      <td>{fmtDate(s.startsAt)}</td>
                      <td>{s.endsAt ? fmtDate(s.endsAt) : 'Süresiz'}</td>
                      <td>{s.status === 'ACTIVE' ? 'Aktif' : s.status === 'CANCELLED' ? 'İptal edildi' : 'Süresi doldu'}</td>
                    </tr>
                  ))}
                </tbody>
              </table>
            )}
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
