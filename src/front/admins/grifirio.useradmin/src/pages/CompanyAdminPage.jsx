import { useCallback, useEffect, useState } from 'react';
import { useNavigate } from 'react-router-dom';
import { useKeycloak } from '@react-keycloak/web';
import {
  COMPANY_ROLES,
  createSubCompany,
  describeError,
  fetchCompanies,
  fetchCompanyUsers,
  roleName,
} from '../services/companyService';
import '../styles/CompanyAdminPage.css';

const TABS = [
  { key: 'info', label: 'Şirket bilgileri' },
  { key: 'users', label: 'Yetkili kullanıcılar' },
  { key: 'tree', label: 'Alt şirketler' },
];

export default function CompanyAdminPage() {
  const navigate = useNavigate();
  const { keycloak } = useKeycloak();
  const token = keycloak.token;
  const companyId = keycloak.tokenParsed?.company_id;

  // Ust menudeki "Yetkili kullanicilar" / "Alt sirketler" girisleri buraya
  // dogrudan ilgili sekmeyle geliyor; ?tab olmadan hep "info" acilirdi.
  const [tab, setTab] = useState(
    () => new URLSearchParams(window.location.search).get('tab') || 'info'
  );
  const [companies, setCompanies] = useState([]);
  const [users, setUsers] = useState([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState('');
  const [notice, setNotice] = useState('');

  const company = companies.find((c) => c.id === companyId) || null;
  const children = companies.filter((c) => c.parentCompanyId === companyId);

  const load = useCallback(async () => {
    if (!companyId) {
      setLoading(false);
      return;
    }
    setLoading(true);
    setError('');
    try {
      const [list, members] = await Promise.all([
        fetchCompanies(token),
        fetchCompanyUsers(token, companyId),
      ]);
      setCompanies(list);
      setUsers(members);
    } catch (err) {
      setError(describeError(err, 'Şirket bilgileri okunamadı.'));
    } finally {
      setLoading(false);
    }
  }, [token, companyId]);

  useEffect(() => {
    load();
  }, [load]);

  if (!companyId) {
    return (
      <div className="ca">
        <div className="ca-empty">
          <h2>Şirket bulunamadı</h2>
          <p>Hesabınız henüz bir çalışma alanına bağlı değil.</p>
          <a className="ca-btn" href="/onboarding">Çalışma alanı kur</a>
        </div>
      </div>
    );
  }

  return (
    <div className="ca">
      <header className="ca-head">
        <div>
          <p className="ca-eyebrow">Şirket yönetimi</p>
          <h1>{company?.name || 'Şirketiniz'}</h1>
          {company?.code && <span className="ca-code">{company.code}</span>}
        </div>
        <button className="ca-btn ca-btn--ghost" onClick={load} disabled={loading}>
          {loading ? 'Yükleniyor…' : 'Yenile'}
        </button>
      </header>

      <nav className="ca-tabs">
        {TABS.map((t) => (
          <button
            key={t.key}
            className={tab === t.key ? 'is-active' : ''}
            onClick={() => setTab(t.key)}
          >
            {t.label}
          </button>
        ))}
      </nav>

      {error && <div className="ca-alert ca-alert--error">{error}</div>}
      {notice && <div className="ca-alert ca-alert--ok">{notice}</div>}

      {tab === 'info' && <InfoTab company={company} childCount={children.length} users={users} />}

      {tab === 'users' && (
        <UsersTab
          users={users}
          loading={loading}
          currentUserId={keycloak.tokenParsed?.sub}
          onManage={() => navigate('/settings/user')}
        />
      )}

      {tab === 'tree' && (
        <SubCompaniesTab
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

function InfoTab({ company, childCount, users }) {
  const rows = [
    ['Şirket adı', company?.name || '—'],
    ['Kısa kod', company?.code || '—'],
    ['Açıklama', company?.description || '—'],
    ['Hiyerarşi seviyesi', company?.level ?? 0],
    ['Alt şirket sayısı', childCount],
    ['Yetkili kullanıcı', users.length],
    ['Durum', company?.isActive === false ? 'Pasif' : 'Aktif'],
  ];

  return (
    <section className="ca-panel">
      <dl className="ca-rows">
        {rows.map(([k, v]) => (
          <div key={k}>
            <dt>{k}</dt>
            <dd>{v}</dd>
          </div>
        ))}
      </dl>

      <p className="ca-note">
        Şirket bilgilerini düzenleme ve döküman yükleme henüz açık değil — bunlar için
        Identity tarafında güncelleme ve dosya uçları gerekiyor.
      </p>
    </section>
  );
}

/**
 * Sirketin kullanici listesi — yalnizca okuma.
 *
 * Rol degistirme ve erisim kaldirma Ayarlar › Kullanici Ayarlari'nda.
 * Ikisi de duzenleyebilir olsaydi menude ayni isi yapan iki baslik olurdu;
 * burasi "kimler var" sorusuna, oteki "kim ne yapabilir" sorusuna bakiyor.
 */
function UsersTab({ users, loading, currentUserId, onManage }) {
  if (loading) return <section className="ca-panel">Yükleniyor…</section>;

  return (
    <section className="ca-panel">
      <div className="ca-panel-head">
        <h2>Yetkili kullanıcılar</h2>
        <button className="ca-btn ca-btn--ghost" onClick={onManage}>
          Kullanıcı ayarları →
        </button>
      </div>

      {users.length === 0 ? (
        <p className="ca-note">Bu şirkette tanımlı kullanıcı yok.</p>
      ) : (
        <table className="ca-table">
          <thead>
            <tr>
              <th>Kullanıcı</th>
              <th>Yetki</th>
              <th>Tanımlanma</th>
            </tr>
          </thead>
          <tbody>
            {users.map((u) => {
              const isSelf = u.keycloakUserId === currentUserId;
              return (
                <tr key={u.keycloakUserId}>
                  <td>
                    <span className="ca-user">{u.email || u.userName || u.keycloakUserId}</span>
                    {isSelf && <span className="ca-self">siz</span>}
                  </td>
                  <td>{roleName(u.role)}</td>
                  <td className="ca-dim">
                    {u.assignedAt ? new Date(u.assignedAt).toLocaleDateString('tr-TR') : '—'}
                  </td>
                </tr>
              );
            })}
          </tbody>
        </table>
      )}

      <div className="ca-roles">
        {COMPANY_ROLES.map((r) => (
          <div key={r.code}>
            <strong>{r.name}</strong>
            <span>{r.description}</span>
          </div>
        ))}
      </div>
    </section>
  );
}

function SubCompaniesTab({ token, parentId, children, onCreated, setError, setNotice }) {
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
    <section className="ca-panel">
      {children.length === 0 ? (
        <p className="ca-note">Henüz alt şirket yok.</p>
      ) : (
        <ul className="ca-list">
          {children.map((c) => (
            <li key={c.id}>
              <span className="ca-user">{c.name}</span>
              {c.code && <span className="ca-code">{c.code}</span>}
              <span className="ca-dim">seviye {c.level}</span>
            </li>
          ))}
        </ul>
      )}

      <form className="ca-form" onSubmit={submit}>
        <h3>Alt şirket ekle</h3>
        <div className="ca-form-row">
          <label>
            <span>Ad</span>
            <input
              value={form.name}
              onChange={(e) => setForm({ ...form, name: e.target.value })}
              placeholder="Akdeniz Tekstil Lojistik"
            />
          </label>
          <label>
            <span>Kısa kod</span>
            <input
              value={form.code}
              onChange={(e) => setForm({ ...form, code: e.target.value })}
              placeholder="AKD-LOJ"
            />
          </label>
        </div>
        <button className="ca-btn" type="submit" disabled={busy}>
          {busy ? 'Ekleniyor…' : 'Ekle'}
        </button>
      </form>
    </section>
  );
}
