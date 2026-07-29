import { useCallback, useEffect, useState } from 'react';
import { Link, useParams } from 'react-router-dom';
import {
  cancelSubscription,
  createSubscription,
  describeError,
  getCompanies,
  getCompanySubscriptions,
  getCompanyUsers,
  revokeRole,
} from '../services/identityApi';

const PLANS = ['TRIAL', 'STANDARD', 'ENTERPRISE'];

const formatDate = (value) =>
  value ? new Date(value).toLocaleDateString('tr-TR') : '—';

export default function CompanyDetailPage() {
  const { companyId } = useParams();
  const [company, setCompany] = useState(null);
  const [users, setUsers] = useState([]);
  const [subscriptions, setSubscriptions] = useState([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState('');
  const [busy, setBusy] = useState('');
  const [plan, setPlan] = useState('STANDARD');

  const load = useCallback(async () => {
    setLoading(true);
    setError('');
    try {
      // Firma detayi icin ayri bir uc henuz yok; liste zaten platform ekibine
      // tum firmalari donduruyor, aradigimizi oradan aliyoruz.
      const [all, userRows, subs] = await Promise.all([
        getCompanies(),
        getCompanyUsers(companyId),
        getCompanySubscriptions(companyId),
      ]);
      setCompany(all.find((c) => c.id === companyId) || null);
      setUsers(userRows);
      setSubscriptions(subs);
    } catch (err) {
      setError(describeError(err));
    } finally {
      setLoading(false);
    }
  }, [companyId]);

  useEffect(() => {
    load();
  }, [load]);

  const run = async (key, action) => {
    setBusy(key);
    setError('');
    try {
      await action();
      await load();
    } catch (err) {
      setError(describeError(err));
    } finally {
      setBusy('');
    }
  };

  const activeSubscription = subscriptions.find((s) => s.isCurrentlyActive);

  if (loading) {
    return (
      <div className="gf-page gf-stack">
        <div className="gf-skeleton gf-skeleton--title" />
        <div className="gf-skeleton gf-skeleton--line" />
        <div className="gf-skeleton gf-skeleton--line gf-skeleton--short" />
      </div>
    );
  }

  return (
    <div className="gf-page">
      <div className="gf-page-header">
        <div>
          <Link to="/companies" className="gf-text-sm gf-muted">← Firmalar</Link>
          <h1 className="gf-page-title">{company?.name || 'Firma'}</h1>
          <p className="gf-page-subtitle">
            {company?.code ? `${company.code} · ` : ''}
            {company?.parentCompanyId ? `Alt firma (seviye ${company.level})` : 'Kök firma'}
          </p>
        </div>
      </div>

      {error && <div className="gf-alert gf-alert--danger">{error}</div>}

      <section className="gf-card pa-section">
        <div className="gf-card__header">
          <h3>Erişim</h3>
          {activeSubscription ? (
            <span className="gf-badge gf-badge--success">
              {activeSubscription.plan} · aktif
            </span>
          ) : (
            <span className="gf-badge gf-badge--danger">abonelik yok</span>
          )}
        </div>
        <div className="gf-card__body gf-stack">
          <p className="gf-muted gf-text-sm">
            Aktif aboneliği olmayan firmanın kullanıcıları ürünü kullanamaz.
          </p>

          {!activeSubscription && (
            <div className="gf-row">
              <select
                className="gf-select"
                value={plan}
                onChange={(e) => setPlan(e.target.value)}
                style={{ maxWidth: 220 }}
              >
                {PLANS.map((p) => <option key={p} value={p}>{p}</option>)}
              </select>
              <button
                className="gf-btn gf-btn--primary"
                disabled={busy === 'create-sub'}
                onClick={() => run('create-sub', () => createSubscription({ companyId, plan }))}
              >
                {busy === 'create-sub' ? <><span className="gf-spinner" /> Açılıyor…</> : 'Abonelik aç'}
              </button>
            </div>
          )}

          {subscriptions.length > 0 && (
            <table className="pa-table">
              <thead>
                <tr><th>Plan</th><th>Durum</th><th>Başlangıç</th><th>Bitiş</th><th>Açan</th><th /></tr>
              </thead>
              <tbody>
                {subscriptions.map((s) => (
                  <tr key={s.id}>
                    <td>{s.plan}</td>
                    <td>
                      <span className={`gf-badge ${s.isCurrentlyActive ? 'gf-badge--success' : ''}`}>
                        {s.status}
                      </span>
                    </td>
                    <td>{formatDate(s.startsAt)}</td>
                    <td>{formatDate(s.endsAt)}</td>
                    <td className="gf-muted">{s.createdBy || '—'}</td>
                    <td>
                      {s.isCurrentlyActive && (
                        <button
                          className="gf-btn gf-btn--sm gf-btn--danger"
                          disabled={busy === `cancel-${s.id}`}
                          onClick={() => run(`cancel-${s.id}`, () => cancelSubscription(s.id))}
                        >
                          İptal et
                        </button>
                      )}
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          )}
        </div>
      </section>

      <section className="gf-card pa-section">
        <div className="gf-card__header">
          <h3>Kullanıcılar</h3>
          <span className="gf-badge">{users.length}</span>
        </div>
        <div className="gf-card__body">
          {users.length === 0 ? (
            <p className="gf-muted gf-text-sm">Bu firmada yetkili kullanıcı yok.</p>
          ) : (
            <table className="pa-table">
              <thead>
                <tr><th>Kullanıcı</th><th>Rol</th><th>Atayan</th><th>Tarih</th><th /></tr>
              </thead>
              <tbody>
                {users.map((u) => (
                  <tr key={u.id}>
                    <td className="pa-mono">{u.keycloakUserId}</td>
                    <td><span className="gf-badge">{u.role}</span></td>
                    <td className="gf-muted">{u.assignedBy || '—'}</td>
                    <td className="gf-muted">{formatDate(u.assignedAt)}</td>
                    <td>
                      <button
                        className="gf-btn gf-btn--sm gf-btn--danger"
                        disabled={busy === `revoke-${u.id}`}
                        onClick={() => run(`revoke-${u.id}`, () => revokeRole(u.keycloakUserId, companyId))}
                      >
                        Yetkiyi al
                      </button>
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          )}
        </div>
      </section>
    </div>
  );
}
