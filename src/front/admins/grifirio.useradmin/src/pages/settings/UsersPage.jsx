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
  const companyId = keycloak.tokenParsed?.company_id;
  const currentUserId = keycloak.tokenParsed?.sub;

  const [users, setUsers] = useState([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState('');
  const [notice, setNotice] = useState('');
  const [busyUser, setBusyUser] = useState(null);
  const [search, setSearch] = useState('');
  const [roleFilter, setRoleFilter] = useState('ALL');

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

  // Identity'nin listeleme ucu (UserCompanyRoleDto) ad ve e-posta tasimiyor,
  // yalnizca Keycloak kimligi geliyor. O yuzden gorunen isim yerine kimligin
  // ilk parcasini gosteriyoruz; uydurma bir ad uretmektense ne oldugu belli
  // olsun. Uc bu alanlari dondurmeye baslayinca satir kendiliginden duzelir.
  const displayName = (u) => u.userName || u.email || 'Kullanıcı kaydı';

  const initials = (u) => {
    const source = u.userName || u.email;
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
              erişiminizi kaldıramazsınız — şirket yöneticisiz kalabilirdi. Ad ve e-posta
              sütunları, Identity’nin listeleme ucu bu alanları taşımadığı için kimlik numarası
              gösteriyor.
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
                            disabled={isSelf || busy}
                            onChange={(e) => changeRole(u, e.target.value)}
                            aria-label={`${u.email || u.userName} rolü`}
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
                          style={{ color: 'var(--gf-danger)' }}
                          disabled={isSelf || busy}
                          onClick={() => removeUser(u)}
                        >
                          {busy ? 'İşleniyor…' : 'Erişimi kaldır'}
                        </button>
                      </td>
                    </tr>
                  );
                })}
            </tbody>
          </table>
        </div>
      </section>

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
