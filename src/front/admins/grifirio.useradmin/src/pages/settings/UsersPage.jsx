import { useCallback, useEffect, useMemo, useState } from 'react';
import { useKeycloak } from '@react-keycloak/web';
import {
  describeError,
  fetchCompanyUsers,
  fetchPermissionActions,
  registerUser,
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

  const canCreateUser = can('USERS.CREATE');
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
  const [addOpen, setAddOpen] = useState(false);
  const [addForm, setAddForm] = useState({ email: '', firstName: '', lastName: '', password: '' });
  const [adding, setAdding] = useState(false);

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

  const addUser = async (e) => {
    e.preventDefault();
    setError('');
    setNotice('');
    setAdding(true);
    try {
      await registerUser(token, { companyId, ...addForm });
      // Kisi uye ve izinsiz basliyor: ne yapabilecegi rolleriyle
      // belirlenecek, o yuzden ekledikten sonra yetki paneline yonlendirmek
      // yerine listeyi tazeleyip mesajda soyluyoruz.
      setNotice(`${addForm.email} eklendi. Kişi ilk girişinde kendi parolasını kuracak; yetkisi için rol atayın.`);
      setAddForm({ email: '', firstName: '', lastName: '', password: '' });
      setAddOpen(false);
      await load();
    } catch (err) {
      setError(describeError(err, 'Kullanıcı eklenemedi.'));
    } finally {
      setAdding(false);
    }
  };

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

          {canCreateUser && !addOpen && (
            <button
              type="button"
              className="st-btn st-btn--sm"
              style={{ marginBottom: 16 }}
              onClick={() => setAddOpen(true)}
            >
              + Kullanıcı ekle
            </button>
          )}

          {canCreateUser && addOpen && (
            <form
              onSubmit={addUser}
              style={{ display: 'flex', flexDirection: 'column', gap: 12, marginBottom: 20 }}
            >
              <p className="st-caps" style={{ margin: 0 }}>Yeni kullanıcı</p>
              <div className="st-form-grid">
                <label className="st-field">
                  <span>E-posta</span>
                  <input
                    type="email"
                    required
                    value={addForm.email}
                    onChange={(e) => setAddForm({ ...addForm, email: e.target.value })}
                    placeholder="ad.soyad@sirket.com"
                    autoFocus
                  />
                </label>
                <label className="st-field">
                  <span>Ad</span>
                  <input
                    required
                    value={addForm.firstName}
                    onChange={(e) => setAddForm({ ...addForm, firstName: e.target.value })}
                  />
                </label>
                <label className="st-field">
                  <span>Soyad</span>
                  <input
                    required
                    value={addForm.lastName}
                    onChange={(e) => setAddForm({ ...addForm, lastName: e.target.value })}
                  />
                </label>
                <label className="st-field">
                  <span>Geçici parola</span>
                  <input
                    required
                    minLength={8}
                    value={addForm.password}
                    onChange={(e) => setAddForm({ ...addForm, password: e.target.value })}
                  />
                  <small className="st-hint">
                    Kişi ilk girişinde kendi parolasını kurar; bu değer kalıcı değil.
                  </small>
                </label>
              </div>
              <div style={{ display: 'flex', gap: 10 }}>
                <button type="submit" className="st-btn st-btn--sm" disabled={adding}>
                  {adding ? 'Ekleniyor…' : 'Ekle'}
                </button>
                <button
                  type="button"
                  className="st-btn st-btn--ghost st-btn--sm"
                  onClick={() => setAddOpen(false)}
                >
                  Vazgeç
                </button>
              </div>
              <small className="st-hint">
                Eklenen kişi üye olarak başlar ve hiçbir izni olmaz. Ne yapabileceğini
                “Yetkiler”den rol atayarak belirlersiniz.
              </small>
            </form>
          )}

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
                    <th>E-posta</th>
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
                        <td className="st-strong">{displayName(u)}</td>
                        <td className="st-dim">{u.email || '—'}</td>
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
                              className="st-select"
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
                          <span className="st-row-actions">
                          <button
                            type="button"
                            className="st-link"
                            onClick={() => setOpenUser(u)}
                          >
                            Yetkiler
                          </button>
                          {canManageMembership && !isFounder && (
                            <button
                              type="button"
                              className="st-link"
                              disabled={busy}
                              onClick={() => removeUser(u)}
                            >
                              Çıkar
                            </button>
                          )}
                          </span>
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
        <UserAccessModal
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
 * Bir kişinin şirketteki yetkisi: rolleri ve izinleri.
 *
 * Modal olarak açılıyor, sayfanın altına eklenen bir bölüm olarak değil: liste
 * uzunken panel görüş alanının dışında açılıyor ve rol anahtarına basınca
 * hiçbir şey olmamış gibi görünüyordu.
 *
 * Tek bir izin matrisi var. Önceden "kişisel izinler" ve "etkin izinler" diye
 * iki matris vardı ve ikisi aynı şeyi iki kez gösteriyordu. Şimdi tek matriste
 * rolden gelen izinler açık ama kilitli — kaldırmak için rolü geri almak
 * gerekiyor — kişisel izinler ise düzenlenebilir. Açık olan her anahtar zaten
 * etkin izindir.
 */
function UserAccessModal({
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

  // Escape ile kapanmak bir modalin en temel beklentisi.
  useEffect(() => {
    const onKey = (e) => {
      if (e.key === 'Escape') onClose();
    };
    window.addEventListener('keydown', onKey);
    return () => window.removeEventListener('keydown', onKey);
  }, [onClose]);

  const toggleRole = async (role, assigned) => {
    setBusy(true);
    setError('');
    setNotice('');
    try {
      if (assigned) {
        await removeUserFromRole(token, role.id, user.keycloakUserId);
        setNotice(role.name + ' rolü kaldırıldı.');
      } else {
        await assignUserToRole(token, role.id, user.keycloakUserId);
        setNotice(role.name + ' rolü verildi.');
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
      setNotice('İzinler kaydedildi.');
      await load();
    } catch (err) {
      setError(describeError(err, 'İzinler kaydedilemedi.'));
    } finally {
      setBusy(false);
    }
  };

  const assignedRoleIds = new Set((access?.roles ?? []).map((r) => r.id));
  const fromRoles = [...new Set((access?.roles ?? []).flatMap((r) => r.permissions ?? []))];
  const dirty =
    JSON.stringify([...personal].sort()) !==
    JSON.stringify([...(access?.personalPermissions ?? [])].sort());

  const editable = !loading && access && !access.bypassesPermissions;

  return (
    <div
      className="st-modal-overlay"
      role="presentation"
      onClick={(e) => {
        if (e.target === e.currentTarget) onClose();
      }}
    >
      <div className="st-modal" role="dialog" aria-modal="true">
        <div className="st-modal-head">
          <div>
            <h2>{label}</h2>
            <p className="st-card-sub">Rolleri ve izinleri.</p>
          </div>
          <button type="button" className="st-link" onClick={onClose}>
            Kapat
          </button>
        </div>

        <div className="st-modal-body">
          {loading && <p className="st-empty">Yükleniyor…</p>}

          {!loading && access?.bypassesPermissions && (
            <p className="st-hint">
              <strong>{levelLabel(access.level)}</strong> izin kümesinin dışında: her şeye erişir,
              rol ya da izinle sınırlandırılamaz. Sınırlandırmak için önce üyelik seviyesini Üye
              yapın.
            </p>
          )}

          {editable && (
            <>
              <p className="st-caps" style={{ marginBottom: 10 }}>
                Roller
              </p>
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

              <div className="st-modal-section">
                <p className="st-caps" style={{ marginBottom: 4 }}>
                  İzinler ({access.effectivePermissions?.length ?? 0})
                </p>
                <p className="st-hint" style={{ marginBottom: 12 }}>
                  Kesikli çerçeveli anahtarlar bir rolden geliyor ve buradan kapatılamaz —
                  kaldırmak için rolü geri alın. Diğerleri kişiye özel.
                </p>
                <PermissionMatrix
                  catalog={catalog}
                  value={personal}
                  locked={fromRoles}
                  onChange={canManagePermissions ? setPersonal : undefined}
                />
              </div>
            </>
          )}
        </div>

        {editable && canManagePermissions && (
          <div className="st-modal-foot">
            <button
              type="button"
              className="st-btn st-btn--sm"
              disabled={busy || !dirty}
              onClick={savePersonal}
            >
              {busy ? 'Kaydediliyor…' : 'İzinleri kaydet'}
            </button>
            <button type="button" className="st-btn st-btn--ghost st-btn--sm" onClick={onClose}>
              Kapat
            </button>
          </div>
        )}
      </div>
    </div>
  );
}
