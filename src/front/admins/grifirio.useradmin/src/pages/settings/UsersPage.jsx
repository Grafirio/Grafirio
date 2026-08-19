import { useCallback, useEffect, useMemo, useState } from 'react';
import { useKeycloak } from '@react-keycloak/web';
import {
  COMPANY_ROLES,
  assignRole,
  describeError,
  fetchCompanyUsers,
  revokeRole,
  roleName,
} from '../../services/companyService';
import { useCompany } from '../../contexts/companyContext';
import '../../styles/SettingsPages.css';

/**
 * Ayarlar › Kullanicilar (Kullanicilar sekmesi).
 *
 * Kullanici yonetiminin yapildigi yer burasi: rol degistirme ve erisim
 * kaldirma. Arama/rol filtresi sunucuya gitmiyor — zaten cekilmis listeyi
 * tarayicida suzuyor, bu yuzden uydurma bir "arama servisi" gerekmiyor.
 *
 * Davet butonu (ust basliktaki, UsersRolesPage.jsx) kapali: mevcut kayit
 * ucu, yoneticinin baskasi adina parola belirlemesini istiyor. Dogrusu
 * e-posta daveti gonderip kisinin kendi parolasini kurmasi; o uc yazilana
 * kadar buraya yarim bir akis koymadim.
 */
// embedded: UsersRolesPage bu bileseni "Kullanicilar" sekmesinde gosterir ve
// kendi sayfa basligini kendisi cizer.
export default function UsersPage({ embedded = false } = {}) {
  const { keycloak } = useKeycloak();
  const token = keycloak.token;
  const currentUserId = keycloak.tokenParsed?.sub;

  // Sirket, token'daki company_id degil Nav'daki secici: ayni kullanici bir
  // subede yonetici, baskasinda siradan kullanici olabiliyor. Token'dan
  // okundugunda sube degistirmek listeyi hic etkilemiyordu — panelin geri
  // kalani secili sirketle calisirken burasi baska bir sirketi gosteriyordu.
  const { selected: company, companies, can } = useCompany();
  const companyId = company?.id;

  const canManageRoles = can('USERS_ROLES.MANAGE_ROLES');

  const [users, setUsers] = useState([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState('');
  const [notice, setNotice] = useState('');
  const [busyUser, setBusyUser] = useState(null);
  const [search, setSearch] = useState('');
  const [roleFilter, setRoleFilter] = useState('ALL');
  const [companiesFor, setCompaniesFor] = useState(null);

  const load = useCallback(async () => {
    if (!companyId) {
      setLoading(false);
      return;
    }
    setLoading(true);
    setError('');
    try {
      setUsers(await fetchCompanyUsers(token, companyId));
    } catch (err) {
      setError(describeError(err, 'Kullanıcılar okunamadı.'));
    } finally {
      setLoading(false);
    }
  }, [token, companyId]);

  useEffect(() => {
    load();
  }, [load]);

  const changeRole = async (user, role) => {
    setError('');
    setNotice('');
    setBusyUser(user.keycloakUserId);
    try {
      await assignRole(token, { keycloakUserId: user.keycloakUserId, companyId, role });
      setNotice(`Yetki güncellendi: ${roleName(role)}`);
      await load();
    } catch (err) {
      setError(describeError(err, 'Yetki güncellenemedi.'));
    } finally {
      setBusyUser(null);
    }
  };

  const removeUser = async (user) => {
    if (!window.confirm('Bu kullanıcının şirkete erişimi kaldırılsın mı?')) {
      return;
    }
    setError('');
    setNotice('');
    setBusyUser(user.keycloakUserId);
    try {
      await revokeRole(token, { keycloakUserId: user.keycloakUserId, companyId });
      setNotice('Kullanıcının erişimi kaldırıldı.');
      await load();
    } catch (err) {
      setError(describeError(err, 'Erişim kaldırılamadı.'));
    } finally {
      setBusyUser(null);
    }
  };

  // Ad ve e-posta artik uctan geliyor: Identity bunlari Keycloak'tan okuyup
  // dondurmeye basladi (KeycloakUserDirectory). Kullanici Keycloak'ta
  // bulunamazsa — silinmis olabilir, yetki kaydi denetim izi olarak duruyor —
  // kimlik gosteriliyor; uydurma bir ad uretmektense ne oldugu belli olsun.
  const displayName = (u) => u.displayName || u.email || u.keycloakUserId;

  const initials = (u) => {
    const source = u.displayName || u.email;
    if (!source) return '#';
    return source
      .replace(/@.*/, '')
      .split(/[.\s_-]+/)
      .map((p) => p[0])
      .slice(0, 2)
      .join('')
      .toUpperCase();
  };

  const adminCount = users.filter((u) => u.role === 'COMPANY_ADMIN').length;

  const visibleUsers = useMemo(() => {
    const q = search.trim().toLowerCase();
    return users.filter((u) => {
      if (roleFilter !== 'ALL' && u.role !== roleFilter) return false;
      if (!q) return true;
      return displayName(u).toLowerCase().includes(q) || (u.email || '').toLowerCase().includes(q);
    });
  }, [users, search, roleFilter]);

  const wrapClass = embedded ? 'st-embed' : 'st';

  if (!companyId) {
    return (
      <div className={wrapClass}>
        {!embedded && (
          <div className="st-head">
            <div>
              <p className="st-eyebrow">Ayarlar · Kullanıcılar</p>
              <h1>Kullanıcı Ayarları</h1>
            </div>
          </div>
        )}
        <div className="st-card">
          <p className="st-empty">Hesabınız henüz bir şirkete bağlı değil.</p>
        </div>
      </div>
    );
  }

  return (
    <div className={wrapClass}>
      {!embedded && (
        <div className="st-head">
          <div>
            <p className="st-eyebrow">Ayarlar · Kullanıcılar</p>
            <h1>Kullanıcı Ayarları</h1>
            <p className="st-lead">
              Şirketinizdeki kullanıcıların rollerini değiştirin ya da erişimlerini kaldırın.
            </p>
          </div>
        </div>
      )}

      {error && <div className="st-alert">{error}</div>}
      {notice && <div className="st-ok">{notice}</div>}

      <div className="st-grid-4">
        <div className="st-kpi">
          <span className="st-kpi-label">Toplam kullanıcı</span>
          <strong>{loading ? '—' : users.length}</strong>
          <span className="st-kpi-note">şirkete tanımlı</span>
        </div>
        <div className="st-kpi" style={{ '--accent-line': 'var(--gf-teal)' }}>
          <span className="st-kpi-label">Yönetici</span>
          <strong>{loading ? '—' : adminCount}</strong>
          <span className="st-kpi-note">rol atayabilen</span>
        </div>
        <div className="st-kpi" style={{ '--accent-line': 'var(--gf-sun)' }}>
          <span className="st-kpi-label">Müdür</span>
          <strong>{loading ? '—' : users.filter((u) => u.role === 'COMPANY_MANAGER').length}</strong>
          <span className="st-kpi-note">veri kaynağı yöneten</span>
        </div>
        <div className="st-kpi" style={{ '--accent-line': 'var(--gf-c06)' }}>
          <span className="st-kpi-label">Kullanıcı</span>
          <strong>{loading ? '—' : users.filter((u) => u.role === 'COMPANY_USER').length}</strong>
          <span className="st-kpi-note">yalnızca görüntüleyen</span>
        </div>
      </div>

      <section className="st-card">
        <div className="st-card-head">
          <div>
            <h2>{loading ? 'Kullanıcılar' : `${users.length} kullanıcı`}</h2>
            <p className="st-card-sub">
              Rol değişikliği anında geçerli olur. Kendi rolünüzü değiştiremez ya da kendi
              erişiminizi kaldıramazsınız — şirket yöneticisiz kalabilirdi. “Firmalar” bağlantısı
              kullanıcının hangi firmalarda yetkili olduğunu gösterir; buradaki liste yalnızca{' '}
              <strong>{company?.name ?? 'seçili firma'}</strong> içindir.
            </p>
          </div>
          <span style={{ display: 'flex', gap: 8, marginLeft: 'auto', flexWrap: 'wrap' }}>
            <label className="st-field" style={{ maxWidth: 220 }}>
              <input
                type="search"
                placeholder="İsim veya e-posta"
                value={search}
                onChange={(e) => setSearch(e.target.value)}
              />
            </label>
            <label className="st-field" style={{ maxWidth: 170 }}>
              <select value={roleFilter} onChange={(e) => setRoleFilter(e.target.value)}>
                <option value="ALL">Tüm roller</option>
                {COMPANY_ROLES.map((r) => (
                  <option key={r.code} value={r.code}>
                    {r.name}
                  </option>
                ))}
              </select>
            </label>
            <button type="button" className="st-btn st-btn--ghost st-btn--sm" onClick={load} disabled={loading}>
              {loading ? 'Yükleniyor…' : 'Yenile'}
            </button>
          </span>
        </div>

        <div className="st-table-wrap">
          <table className="st-table">
            <thead>
              <tr>
                <th>Kullanıcı</th>
                <th>Rol</th>
                <th>Tanımlanma</th>
                <th className="st-right">İşlem</th>
              </tr>
            </thead>
            <tbody>
              {loading && (
                <tr className="st-table-empty">
                  <td colSpan={4}>Yükleniyor…</td>
                </tr>
              )}

              {!loading && visibleUsers.length === 0 && (
                <tr className="st-table-empty">
                  <td colSpan={4}>
                    {users.length === 0
                      ? 'Bu şirkette tanımlı kullanıcı yok.'
                      : 'Aramayla eşleşen kullanıcı yok.'}
                  </td>
                </tr>
              )}

              {!loading &&
                visibleUsers.map((u) => {
                  const isSelf = u.keycloakUserId === currentUserId;
                  const busy = busyUser === u.keycloakUserId;
                  return (
                    <tr key={u.keycloakUserId}>
                      <td>
                        <span className="st-person">
                          <span className="st-avatar">{initials(u)}</span>
                          <span className="st-person-text">
                            <strong>{displayName(u)}</strong>
                            <span>{u.email || u.keycloakUserId}</span>
                          </span>
                          {isSelf && (
                            <span className="st-badge" style={{ marginLeft: 4 }}>
                              siz
                            </span>
                          )}
                        </span>
                      </td>
                      <td>
                        <label className="st-field" style={{ maxWidth: 190 }}>
                          <select
                            value={u.role}
                            // Rol atamak ayri bir izin: kullanici acabilen
                            // mudur, kimin yonetici olacagina karar vermiyor.
                            disabled={isSelf || busy || !canManageRoles}
                            onChange={(e) => changeRole(u, e.target.value)}
                            aria-label={`${displayName(u)} rolü`}
                          >
                            {COMPANY_ROLES.map((r) => (
                              <option key={r.code} value={r.code}>
                                {r.name}
                              </option>
                            ))}
                          </select>
                        </label>
                      </td>
                      <td className="st-mono st-dim">
                        {u.assignedAt ? new Date(u.assignedAt).toLocaleDateString('tr-TR') : '—'}
                      </td>
                      <td className="st-right">
                        <button
                          type="button"
                          className="st-link"
                          onClick={() => setCompaniesFor(u)}
                        >
                          Firmalar
                        </button>{' '}
                        {canManageRoles && (
                          <button
                            type="button"
                            className="st-link"
                            style={{ color: 'var(--gf-danger)' }}
                            disabled={isSelf || busy}
                            onClick={() => removeUser(u)}
                          >
                            {busy ? 'İşleniyor…' : 'Erişimi kaldır'}
                          </button>
                        )}
                      </td>
                    </tr>
                  );
                })}
            </tbody>
          </table>
        </div>
      </section>

      {companiesFor && (
        <UserCompaniesPanel
          token={token}
          user={companiesFor}
          userLabel={displayName(companiesFor)}
          companies={companies}
          canManageRoles={canManageRoles}
          isSelf={companiesFor.keycloakUserId === currentUserId}
          onClose={() => setCompaniesFor(null)}
          onChanged={load}
          setError={setError}
          setNotice={setNotice}
        />
      )}

      <section className="st-card">
        <div className="st-card-head">
          <h2>Oturum güvenliği</h2>
        </div>
        <div className="st-switch">
          <span className="st-switch-text">
            <strong>İki adımlı doğrulama</strong>
            <span>Keycloak realm ayarlarından yönetilir</span>
          </span>
          <span className="st-switch-knob" aria-hidden="true" />
        </div>
        <div className="st-switch">
          <span className="st-switch-text">
            <strong>Oturum süresi</strong>
            <span>Keycloak realm ayarlarından yönetilir</span>
          </span>
          <span className="st-switch-knob" aria-hidden="true" />
        </div>
        <p className="st-card-sub" style={{ marginTop: 14 }}>
          Oturum kuralları kimlik sağlayıcının kendi ayarları; panelden değiştirilmesi, iki yerde
          birden tutulan ve zamanla ayrışan bir kural seti demek olurdu.
        </p>
      </section>
    </div>
  );
}

/**
 * Bir kullanicinin hangi firmalarda yetkili oldugu.
 *
 * Yetki hiyerarsik: bir sirkette verilen rol o sirketin altindaki subelerde de
 * gecerli. Panel bu yuzden iki seyi ayirt ediyor — dogrudan verilmis rol
 * (kaldirilabilir) ve ustten miras gelen rol (o sirkette kaydi yok, kaldirmak
 * icin ust sirkete gitmek gerekir). Ayrimi gostermezsek "kaldirdim ama hala
 * girebiliyor" gibi gorunur.
 *
 * Roller sirket basina okunuyor: erisilebilir sirket sayisi az ve panel
 * istege bagli aciliyor, dolayisiyla listeyi onden cekip her satir icin
 * bellekte tutmaktansa acildiginda okumak daha dogru.
 */
function UserCompaniesPanel({
  token,
  user,
  userLabel,
  companies,
  canManageRoles,
  isSelf,
  onClose,
  onChanged,
  setError,
  setNotice,
}) {
  const [rows, setRows] = useState([]);
  const [loading, setLoading] = useState(true);
  const [busy, setBusy] = useState(null);

  const load = useCallback(async () => {
    setLoading(true);
    try {
      const found = await Promise.all(
        companies.map(async (c) => {
          // Baska bir sirketin kullanici listesini okuma izni olmayabilir;
          // o satir "bilinmiyor" olarak gecilir, panel yine acilir.
          const members = await fetchCompanyUsers(token, c.id).catch(() => null);
          if (members === null) return { company: c, role: null, readable: false };

          const match = members.find((m) => m.keycloakUserId === user.keycloakUserId);
          return { company: c, role: match?.role ?? null, readable: true };
        })
      );
      setRows(found);
    } finally {
      setLoading(false);
    }
  }, [token, companies, user.keycloakUserId]);

  useEffect(() => {
    load();
  }, [load]);

  const grant = async (companyId, role) => {
    setError('');
    setNotice('');
    setBusy(companyId);
    try {
      await assignRole(token, { keycloakUserId: user.keycloakUserId, companyId, role });
      setNotice('Firma yetkisi güncellendi.');
      await Promise.all([load(), onChanged()]);
    } catch (err) {
      setError(describeError(err, 'Firma yetkisi güncellenemedi.'));
    } finally {
      setBusy(null);
    }
  };

  const revoke = async (companyId) => {
    setError('');
    setNotice('');
    setBusy(companyId);
    try {
      await revokeRole(token, { keycloakUserId: user.keycloakUserId, companyId });
      setNotice('Firma yetkisi kaldırıldı.');
      await Promise.all([load(), onChanged()]);
    } catch (err) {
      setError(describeError(err, 'Firma yetkisi kaldırılamadı.'));
    } finally {
      setBusy(null);
    }
  };

  return (
    <section className="st-card">
      <div className="st-card-head">
        <div>
          <h2>{userLabel} · yetkili firmalar</h2>
          <p className="st-card-sub">
            Bir firmada verilen rol, o firmanın altındaki şubelerde de geçerlidir. Alt satırlarda
            “miras” yazan firmalarda ayrı bir kayıt yok; yetkiyi kaldırmak için üst firmadaki
            kaydı kaldırmak gerekir.
          </p>
        </div>
        <button type="button" className="st-link" onClick={onClose}>
          Kapat
        </button>
      </div>

      {loading && <p className="st-empty">Yükleniyor…</p>}

      {!loading && (
        <div className="st-table-wrap">
          <table className="st-table">
            <thead>
              <tr>
                <th>Firma</th>
                <th>Rol</th>
                <th className="st-right">İşlem</th>
              </tr>
            </thead>
            <tbody>
              {rows.map(({ company, role, readable }) => (
                <tr key={company.id}>
                  <td className="st-strong">
                    {/* Girinti hiyerarsiyi gosteriyor: level 0 kok firma. */}
                    <span style={{ paddingLeft: (company.level ?? 0) * 14 }}>{company.name}</span>
                    {company.code && <span className="st-mono st-dim"> · {company.code}</span>}
                  </td>
                  <td>
                    {!readable ? (
                      <span className="st-dim">okuma izni yok</span>
                    ) : (
                      <label className="st-field" style={{ maxWidth: 190 }}>
                        <select
                          value={role ?? ''}
                          disabled={!canManageRoles || isSelf || busy === company.id}
                          onChange={(e) =>
                            e.target.value ? grant(company.id, e.target.value) : revoke(company.id)
                          }
                          aria-label={`${company.name} rolü`}
                        >
                          <option value="">Yetki yok</option>
                          {COMPANY_ROLES.map((r) => (
                            <option key={r.code} value={r.code}>
                              {r.name}
                            </option>
                          ))}
                        </select>
                      </label>
                    )}
                  </td>
                  <td className="st-right">
                    {readable && role && canManageRoles && !isSelf && (
                      <button
                        type="button"
                        className="st-link"
                        style={{ color: 'var(--gf-danger)' }}
                        disabled={busy === company.id}
                        onClick={() => revoke(company.id)}
                      >
                        {busy === company.id ? 'İşleniyor…' : 'Kaldır'}
                      </button>
                    )}
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}

      {isSelf && (
        <p className="st-hint" style={{ marginTop: 10 }}>
          Kendi firma yetkilerinizi değiştiremezsiniz — şirket yöneticisiz kalabilirdi.
        </p>
      )}
    </section>
  );
}
