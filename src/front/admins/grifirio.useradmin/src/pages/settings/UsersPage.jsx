import { useCallback, useEffect, useMemo, useState } from 'react';
import { useKeycloak } from '@react-keycloak/web';
import {
  describeError,
  fetchCompanyUsers,
  fetchPermissionActions,
  revokeMembership,
  setMembershipLevel,
} from '../../services/companyService';
import {
  assignUserToRole,
  fetchRoles,
  fetchUserAccess,
  removeUserFromRole,
  setUserPermissions,
} from '../../services/roleService';
import { useCompany } from '../../contexts/companyContext';
import {
  ASSIGNABLE_LEVELS,
  LEVEL_HINTS,
  levelLabel,
} from '../../constants/permissions';
import PermissionMatrix from '../../components/organisms/PermissionMatrix';
import '../../styles/SettingsPages.css';

/**
 * Ayarlar › Kullanıcılar.
 *
 * İki ayrı soru burada cevaplanıyor: kim üye (ve hangi seviyede), ve o kişi ne
 * yapabiliyor. İkincisi kişi seçilince açılan yetki panelinde: rolleri,
 * kişisel izinleri ve ikisinin birleşimi.
 *
 * Arama sunucuya gitmiyor — zaten çekilmiş listeyi tarayıcıda süzüyor.
 */
export default function UsersPage({ embedded = false } = {}) {
  const { keycloak } = useKeycloak();
  const token = keycloak.token;

  // Şirket, token'daki company_id değil Nav'daki seçici: aynı kullanıcı bir
  // şubede admin, başkasında üye olabiliyor.
  const { selected: company, can } = useCompany();
  const companyId = company?.id;

  const canManageMembership = can('USERS.MANAGE_MEMBERSHIP');
  const canAssignRoles = can('ROLES.ASSIGN');
  const canManagePermissions = can('ROLES.MANAGE_PERMISSIONS');

  const [users, setUsers] = useState([]);
  const [roles, setRoles] = useState([]);
  const [catalog, setCatalog] = useState([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState('');
  const [notice, setNotice] = useState('');
  const [busyUser, setBusyUser] = useState(null);
  const [search, setSearch] = useState('');
  const [levelFilter, setLevelFilter] = useState('ALL');
  const [openUser, setOpenUser] = useState(null);

  const load = useCallback(async () => {
    if (!companyId) {
      setLoading(false);
      return;
    }
    setLoading(true);
    setError('');
    try {
      const [list, roleList, actions] = await Promise.all([
        fetchCompanyUsers(token, companyId),
        fetchRoles(token, companyId).catch(() => []),
        fetchPermissionActions(token).catch(() => []),
      ]);
      setUsers(list);
      setRoles(roleList);
      setCatalog(actions);
    } catch (err) {
      setError(describeError(err, 'Kullanıcılar okunamadı.'));
    } finally {
      setLoading(false);
    }
  }, [token, companyId]);

  useEffect(() => {
    load();
    setOpenUser(null);
  }, [load]);

  const changeLevel = async (user, level) => {
    setError('');
    setNotice('');
    setBusyUser(user.keycloakUserId);
    try {
      await setMembershipLevel(token, {
        keycloakUserId: user.keycloakUserId,
        companyId,
        level,
      });
      setNotice(`Üyelik seviyesi güncellendi: ${levelLabel(level)}`);
      await load();
    } catch (err) {
      setError(describeError(err, 'Üyelik seviyesi güncellenemedi.'));
    } finally {
      setBusyUser(null);
    }
  };

  const removeUser = async (user) => {
    if (!window.confirm('Bu kullanıcının şirkete erişimi kaldırılsın mı?')) return;

    setError('');
    setNotice('');
    setBusyUser(user.keycloakUserId);
    try {
      await revokeMembership(token, { keycloakUserId: user.keycloakUserId, companyId });
      setNotice('Kullanıcının erişimi kaldırıldı. Rolleri de kapatıldı.');
      if (openUser?.keycloakUserId === user.keycloakUserId) setOpenUser(null);
      await load();
    } catch (err) {
      setError(describeError(err, 'Erişim kaldırılamadı.'));
    } finally {
      setBusyUser(null);
    }
  };

  // Ad ve e-posta uçtan geliyor (KeycloakUserDirectory). Kullanıcı Keycloak'ta
  // bulunamazsa — silinmiş olabilir, üyelik kaydı denetim izi olarak duruyor —
  // kimlik gösteriliyor; uydurma bir ad üretmektense ne olduğu belli olsun.
  const displayName = (u) => u.displayName || u.email || u.keycloakUserId;

  const visibleUsers = useMemo(() => {
    const q = search.trim().toLowerCase();
    return users.filter((u) => {
      if (levelFilter !== 'ALL' && u.level !== levelFilter) return false;
      if (!q) return true;
      return displayName(u).toLowerCase().includes(q) || (u.email || '').toLowerCase().includes(q);
    });
  }, [users, search, levelFilter]);

  const wrapClass = embedded ? 'st-embed' : 'st';

  if (!loading && !companyId) {
    return (
      <div className={wrapClass}>
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
            <p className="st-eyebrow">Ayarlar · Organizasyon</p>
            <h1>Kullanıcılar</h1>
            <p className="st-lead">
              {company ? `${company.name} üyeleri ve yetkileri.` : 'Kim var, ne yapabiliyor.'}
            </p>
          </div>
        </div>
      )}

      {error && <div className="st-alert">{error}</div>}
      {notice && <div className="st-ok">{notice}</div>}
      {loading && <div className="st-card st-empty">Yükleniyor…</div>}

      {!loading && (
        <section className="st-card">
          <div className="st-card-head">
            <div>
              <h2>Üyeler</h2>
              <p className="st-card-sub">
                Kurucu ve adminler izin kümesinin dışında; her şeye erişirler. Üyelerin yetkisi
                rollerinden ve kişisel izinlerinden gelir.
              </p>
            </div>
            <span className="st-head-meta">{users.length} kişi</span>
          </div>

          <div className="st-form-grid" style={{ marginBottom: 16 }}>
            <label className="st-field">
              <span>Ara</span>
              <input
                value={search}
                onChange={(e) => setSearch(e.target.value)}
                placeholder="Ad ya da e-posta"
              />
            </label>
            <label className="st-field">
              <span>Seviye</span>
              <select value={levelFilter} onChange={(e) => setLevelFilter(e.target.value)}>
                <option value="ALL">Hepsi</option>
                <option value="FOUNDER">Kurucu</option>
                <option value="ADMIN">Admin</option>
                <option value="MEMBER">Üye</option>
              </select>
            </label>
          </div>

          {visibleUsers.length === 0 ? (
            <p className="st-empty">Eşleşen kullanıcı yok.</p>
          ) : (
            <div className="st-table-wrap">
              <table className="st-table">
                <thead>
                  <tr>
                    <th>Kişi</th>
                    <th>Seviye</th>
                    <th className="st-right">İşlem</th>
                  </tr>
                </thead>
                <tbody>
                  {visibleUsers.map((u) => {
                    const isFounder = u.level === 'FOUNDER';
                    const busy = busyUser === u.keycloakUserId;

                    return (
                      <tr key={u.keycloakUserId}>
                        <td className="st-strong">
                          {displayName(u)}
                          {u.email && u.displayName && (
                            <span className="st-doc-meta"> · {u.email}</span>
                          )}
                        </td>
                        <td>
                          {isFounder || !canManageMembership ? (
                            <span
                              className={`st-badge ${isFounder ? 'st-badge--ok' : ''}`}
                              title={LEVEL_HINTS[u.level]}
                            >
                              {levelLabel(u.level)}
                            </span>
                          ) : (
                            <select
                              value={u.level}
                              disabled={busy}
                              onChange={(e) => changeLevel(u, e.target.value)}
                            >
                              {ASSIGNABLE_LEVELS.map((level) => (
                                <option key={level} value={level}>
                                  {levelLabel(level)}
                                </option>
                              ))}
                            </select>
                          )}
                        </td>
                        <td className="st-right">
                          <button
                            type="button"
                            className="st-link"
                            onClick={() => setOpenUser(u)}
                          >
                            Yetkiler
                          </button>
                          {canManageMembership && !isFounder && (
                            <>
                              {' '}
                              <button
                                type="button"
                                className="st-link"
                                disabled={busy}
                                onClick={() => removeUser(u)}
                              >
                                Çıkar
                              </button>
                            </>
                          )}
                        </td>
                      </tr>
                    );
                  })}
                </tbody>
              </table>
            </div>
          )}
        </section>
      )}

      {openUser && (
        <UserAccessPanel
          token={token}
          companyId={companyId}
          user={openUser}
          label={displayName(openUser)}
          roles={roles}
          catalog={catalog}
          canAssignRoles={canAssignRoles}
          canManagePermissions={canManagePermissions}
          onClose={() => setOpenUser(null)}
          setError={setError}
          setNotice={setNotice}
        />
      )}
    </div>
  );
}

/**
 * Bir kişinin şirketteki yetkisi: rolleri, kişisel izinleri ve ikisinin
 * birleşimi.
 *
 * Üçü tek uçtan geliyor (GET /permissions/users/...). Ayrı ayrı çekilseydi
 * çok rollü birinde ekranda tutarsız bir tablo görünebilirdi.
 */
function UserAccessPanel({
  token,
  companyId,
  user,
  label,
  roles,
  catalog,
  canAssignRoles,
  canManagePermissions,
  onClose,
  setError,
  setNotice,
}) {
  const [access, setAccess] = useState(null);
  const [personal, setPersonal] = useState([]);
  const [loading, setLoading] = useState(true);
  const [busy, setBusy] = useState(false);

  const load = useCallback(async () => {
    setLoading(true);
    try {
      const result = await fetchUserAccess(token, companyId, user.keycloakUserId);
      setAccess(result);
      setPersonal(result?.personalPermissions ?? []);
    } catch (err) {
      setError(describeError(err, 'Kullanıcının yetkileri okunamadı.'));
    } finally {
      setLoading(false);
    }
  }, [token, companyId, user.keycloakUserId, setError]);

  useEffect(() => {
    load();
  }, [load]);

  const toggleRole = async (role, assigned) => {
    setBusy(true);
    setError('');
    setNotice('');
    try {
      if (assigned) {
        await removeUserFromRole(token, role.id, user.keycloakUserId);
        setNotice(`${role.name} rolü kaldırıldı.`);
      } else {
        await assignUserToRole(token, role.id, user.keycloakUserId);
        setNotice(`${role.name} rolü verildi.`);
      }
      await load();
    } catch (err) {
      setError(describeError(err, 'Rol güncellenemedi.'));
    } finally {
      setBusy(false);
    }
  };

  const savePersonal = async () => {
    setBusy(true);
    setError('');
    setNotice('');
    try {
      await setUserPermissions(token, companyId, user.keycloakUserId, personal);
      setNotice('Kişisel izinler kaydedildi.');
      await load();
    } catch (err) {
      setError(describeError(err, 'Kişisel izinler kaydedilemedi.'));
    } finally {
      setBusy(false);
    }
  };

  const assignedRoleIds = new Set((access?.roles ?? []).map((r) => r.id));
  const fromRoles = (access?.roles ?? []).flatMap((r) => r.permissions ?? []);
  const dirty = JSON.stringify([...personal].sort()) !==
    JSON.stringify([...(access?.personalPermissions ?? [])].sort());

  return (
    <section className="st-card">
      <div className="st-card-head">
        <div>
          <h2>{label} · yetkiler</h2>
          <p className="st-card-sub">
            Etkin izin, rollerin ve kişisel izinlerin birleşimidir.
          </p>
        </div>
        <button type="button" className="st-link" onClick={onClose}>
          Kapat
        </button>
      </div>

      {loading && <p className="st-empty">Yükleniyor…</p>}

      {!loading && access?.bypassesPermissions && (
        <p className="st-hint">
          <strong>{levelLabel(access.level)}</strong> izin kümesinin dışında: her şeye erişir,
          rol ya da kişisel izinle sınırlandırılamaz. Sınırlandırmak için önce üyelik seviyesini
          Üye yapın.
        </p>
      )}

      {!loading && access && !access.bypassesPermissions && (
        <div style={{ display: 'flex', flexDirection: 'column', gap: 22 }}>
          <div>
            <p className="st-caps" style={{ marginBottom: 10 }}>Roller</p>
            {roles.length === 0 ? (
              <p className="st-empty">
                Bu şirkette henüz rol tanımlanmadı. Ayarlar › Roller’den ekleyebilirsiniz.
              </p>
            ) : (
              <div className="st-perm-actions">
                {roles.map((role) => {
                  const assigned = assignedRoleIds.has(role.id);
                  return (
                    <label
                      key={role.id}
                      className="st-perm-switch"
                      data-on={assigned}
                      title={role.description ?? role.name}
                    >
                      <input
                        type="checkbox"
                        checked={assigned}
                        disabled={!canAssignRoles || busy}
                        onChange={() => toggleRole(role, assigned)}
                      />
                      <span className="st-perm-knob" aria-hidden="true" />
                      <span className="st-perm-switch-label">{role.name}</span>
                    </label>
                  );
                })}
              </div>
            )}
          </div>

          <div>
            <p className="st-caps" style={{ marginBottom: 10 }}>Kişisel izinler</p>
            <PermissionMatrix
              catalog={catalog}
              value={personal}
              inherited={fromRoles}
              onChange={canManagePermissions ? setPersonal : undefined}
            />
            <small className="st-hint">
              Rolün dışında kalan tek kişilik durumlar için. Rolden gelen izinler soluk
              gösteriliyor — tekrar vermeye gerek yok.
            </small>
            {canManagePermissions && (
              <div style={{ marginTop: 12 }}>
                <button
                  type="button"
                  className="st-btn st-btn--sm"
                  disabled={busy || !dirty}
                  onClick={savePersonal}
                >
                  {busy ? 'Kaydediliyor…' : 'Kişisel izinleri kaydet'}
                </button>
              </div>
            )}
          </div>

          <div>
            <p className="st-caps" style={{ marginBottom: 10 }}>
              Etkin izinler ({access.effectivePermissions?.length ?? 0})
            </p>
            <PermissionMatrix catalog={catalog} value={access.effectivePermissions ?? []} />
          </div>
        </div>
      )}
    </section>
  );
}
