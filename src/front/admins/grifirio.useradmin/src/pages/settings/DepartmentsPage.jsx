import { useCallback, useEffect, useState } from 'react';
import { useKeycloak } from '@react-keycloak/web';
import {
  describeError,
  fetchCompanyUsers,
  fetchPermissionActions,
  roleName,
} from '../../services/companyService';
import {
  assignUserToDepartment,
  createDepartment,
  deleteDepartment,
  fetchDepartmentMembers,
  fetchDepartments,
  removeUserFromDepartment,
  updateDepartment,
} from '../../services/departmentService';
import { useCompany } from '../../contexts/companyContext';
import { moduleName } from '../../constants/modules';
import { actionLabel, permissionHint } from '../../constants/permissions';
import '../../styles/SettingsPages.css';

const emptyForm = () => ({
  name: '',
  code: '',
  description: '',
  managerKeycloakUserId: '',
  costCenter: '',
  permissions: [],
});

/**
 * Ayarlar › Departmanlar.
 *
 * Departman şirkete (şubeye) bağlı: hangi şirketin departmanlarına bakıldığı
 * Nav'daki şirket değiştiriciden geliyor. Şubeler arası geçiş yetkisini
 * departman değil şirket üyeliği belirliyor — iki kavram karışırsa "hangi
 * şirkete girebilirim" sorusunun iki ayrı cevabı olur.
 *
 * Departman izinleri modül × aksiyon: "veri kaynaklarını görsün ama
 * değiştirmesin" ancak bu kırılımla ifade edilebiliyor. Aksiyon listesi
 * sunucudan geliyor (GET /permissions/actions) — sabit listeyi buraya
 * kopyalamak, ekranda yazan izinle sunucunun uyguladığı iznin sessizce
 * ayrışması demekti.
 *
 * embedded: İzinler sayfası bu bileşeni "Departmanlar" sekmesinde gösterir ve
 * kendi sayfa başlığını kendisi çizer.
 */
export default function DepartmentsPage({ embedded = false } = {}) {
  const { keycloak } = useKeycloak();
  const token = keycloak.token;
  const { selected: company, can } = useCompany();
  const companyId = company?.id;

  // Butonlar izinle gizleniyor ama karar sunucuda: gizlenmiş bir buton isteğin
  // doğrudan gönderilmesini engellemez.
  const canCreate = can('DEPARTMENTS.CREATE');
  const canUpdate = can('DEPARTMENTS.UPDATE');
  const canDelete = can('DEPARTMENTS.DELETE');
  const canAssign = can('DEPARTMENTS.ASSIGN_MEMBERS');

  const [departments, setDepartments] = useState([]);
  const [companyUsers, setCompanyUsers] = useState([]);
  const [actionCatalog, setActionCatalog] = useState([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState('');
  const [notice, setNotice] = useState('');

  const [editingId, setEditingId] = useState(null);
  const [form, setForm] = useState(emptyForm());
  const [formOpen, setFormOpen] = useState(false);
  const [busy, setBusy] = useState(false);

  const [selectedDepartment, setSelectedDepartment] = useState(null);

  const load = useCallback(async () => {
    if (!companyId) {
      setLoading(false);
      return;
    }
    setLoading(true);
    setError('');
    try {
      const [list, users, actions] = await Promise.all([
        fetchDepartments(token, companyId),
        // Üye ekleme listesi buradan: departmana yalnızca şirketin üyeleri
        // atanabiliyor.
        fetchCompanyUsers(token, companyId).catch(() => []),
        // Matris boş kalabilir ama sayfa açılmalı: izin listesi okunamazsa
        // departman adı ve üyeleri hâlâ yönetilebiliyor.
        fetchPermissionActions(token).catch(() => []),
      ]);
      setDepartments(list);
      setCompanyUsers(users);
      setActionCatalog(actions);
    } catch (err) {
      setError(describeError(err, 'Departmanlar okunamadı.'));
    } finally {
      setLoading(false);
    }
  }, [token, companyId]);

  useEffect(() => {
    load();
    setSelectedDepartment(null);
    setFormOpen(false);
    setEditingId(null);
  }, [load]);

  const startCreate = () => {
    setEditingId(null);
    setForm(emptyForm());
    setFormOpen(true);
  };

  const startEdit = (d) => {
    setEditingId(d.id);
    setForm({
      name: d.name ?? '',
      code: d.code ?? '',
      description: d.description ?? '',
      managerKeycloakUserId: d.managerKeycloakUserId ?? '',
      costCenter: d.costCenter ?? '',
      // Sunucu eski kayıtların modül listesini izne çevirip döndürüyor,
      // dolayısıyla burada ayrı bir geriye uyum koduna gerek yok.
      permissions: d.permissions ?? [],
    });
    setFormOpen(true);
  };

  const submit = async (e) => {
    e.preventDefault();
    if (!form.name.trim()) {
      setError('Departman adı gerekli.');
      return;
    }
    setBusy(true);
    setError('');
    setNotice('');
    try {
      const payload = {
        name: form.name.trim(),
        code: form.code.trim() || null,
        description: form.description.trim() || null,
        managerKeycloakUserId: form.managerKeycloakUserId || null,
        costCenter: form.costCenter.trim() || null,
        // Yalnızca izinler gönderiliyor; eski modules alanını sunucu bunlardan
        // türetiyor ki iki alan birbirinden ayrışmasın.
        permissions: form.permissions,
      };

      if (editingId) {
        await updateDepartment(token, editingId, payload);
        setNotice('Departman güncellendi.');
      } else {
        await createDepartment(token, { ...payload, companyId });
        setNotice('Departman eklendi.');
      }

      setFormOpen(false);
      setEditingId(null);
      setForm(emptyForm());
      await load();
    } catch (err) {
      setError(describeError(err, 'Departman kaydedilemedi.'));
    } finally {
      setBusy(false);
    }
  };

  const remove = async (d) => {
    setError('');
    setNotice('');
    try {
      await deleteDepartment(token, d.id);
      setNotice('Departman silindi.');
      if (selectedDepartment?.id === d.id) setSelectedDepartment(null);
      await load();
    } catch (err) {
      setError(describeError(err, 'Departman silinemedi.'));
    }
  };

  const userLabel = (keycloakUserId) => {
    const user = companyUsers.find((u) => u.keycloakUserId === keycloakUserId);
    // Ad ve e-posta artık uçtan geliyor (Keycloak'tan okunuyor). Kullanıcı
    // Keycloak'ta bulunamazsa — silinmiş olabilir, yetki kaydı denetim izi
    // olarak duruyor — kimliğe düşülüyor.
    return user?.displayName || user?.email || keycloakUserId;
  };

  return (
    <div className={embedded ? undefined : 'st'}>
      {/* embedded: sayfa başlığını İzinler sayfası çiziyor, ikinci bir başlık
          sekmenin içinde tekrar olurdu. Ekleme düğmesi yine burada duruyor,
          çünkü yaptığı iş bu sekmeye ait. */}
      <div className="st-head">
        {embedded ? (
          <div>
            <h2>Departmanlar</h2>
            <p className="st-card-sub">
              {company
                ? `${company.name} altındaki organizasyon birimleri ve izinleri.`
                : 'Organizasyon birimleri ve izinleri.'}
            </p>
          </div>
        ) : (
          <div>
            <p className="st-eyebrow">Ayarlar · Organizasyon</p>
            <h1>Departmanlar</h1>
            <p className="st-lead">
              {company
                ? `${company.name} altındaki organizasyon birimleri.`
                : 'Departmanlar, veri erişimi ve rapor dağıtımının temelidir.'}
            </p>
          </div>
        )}
        {!loading && companyId && canCreate && (
          <div className="st-head-actions">
            <button type="button" className="st-btn" onClick={startCreate}>
              + Departman ekle
            </button>
          </div>
        )}
      </div>

      {error && <div className="st-alert">{error}</div>}
      {notice && <div className="st-ok">{notice}</div>}
      {loading && <div className="st-card st-empty">Yükleniyor…</div>}

      {!loading && !companyId && (
        <div className="st-card">
          <p className="st-empty">Hesabınız henüz bir şirkete bağlı değil.</p>
        </div>
      )}

      {!loading && companyId && formOpen && (
        <section className="st-card">
          <p className="st-caps">{editingId ? 'Departmanı düzenle' : 'Yeni departman'}</p>
          <form onSubmit={submit} style={{ display: 'flex', flexDirection: 'column', gap: 14 }}>
            <div className="st-form-grid">
              <label className="st-field">
                <span>Ad</span>
                <input
                  value={form.name}
                  onChange={(e) => setForm({ ...form, name: e.target.value })}
                  placeholder="Muhasebe"
                  autoFocus
                />
              </label>
              <label className="st-field st-field--mono">
                <span>Kod</span>
                <input
                  value={form.code}
                  onChange={(e) => setForm({ ...form, code: e.target.value })}
                  placeholder="MUH"
                />
                <small className="st-hint">Şirket içinde benzersiz olmalı.</small>
              </label>
              <label className="st-field">
                <span>Yönetici</span>
                <select
                  value={form.managerKeycloakUserId}
                  onChange={(e) => setForm({ ...form, managerKeycloakUserId: e.target.value })}
                >
                  <option value="">Seçilmedi</option>
                  {companyUsers.map((u) => (
                    <option key={u.keycloakUserId} value={u.keycloakUserId}>
                      {userLabel(u.keycloakUserId)}
                    </option>
                  ))}
                </select>
                <small className="st-hint">Sorumluyu gösterir; yetki taşımaz.</small>
              </label>
              <label className="st-field st-field--mono">
                <span>Maliyet merkezi</span>
                <input
                  value={form.costCenter}
                  onChange={(e) => setForm({ ...form, costCenter: e.target.value })}
                />
              </label>
            </div>
            <label className="st-field">
              <span>Açıklama</span>
              <textarea
                rows="2"
                value={form.description}
                onChange={(e) => setForm({ ...form, description: e.target.value })}
              />
            </label>

            <fieldset className="st-field" style={{ border: 0, margin: 0, padding: 0 }}>
              <span>İzinler</span>
              {actionCatalog.length === 0 ? (
                <p className="st-empty">İzin listesi okunamadı; kaydedilen izinler korunuyor.</p>
              ) : (
                <div className="st-perm-list">
                  {actionCatalog.map(({ module, permissions: keys }) => {
                    const selectedKeys = keys.filter((k) => form.permissions.includes(k));
                    const all = selectedKeys.length === keys.length && keys.length > 0;
                    const some = selectedKeys.length > 0;

                    return (
                      <div key={module} className="st-perm-module" data-on={some}>
                        <div className="st-perm-module-head">
                          <label>
                            <input
                              type="checkbox"
                              checked={all}
                              // Kismi secim ucuncu bir durum: kutu isaretli
                              // degil ama "hicbiri" de degil. Isaretsiz
                              // gostermek kullaniciya yanlis bilgi verirdi.
                              ref={(el) => {
                                if (el) el.indeterminate = some && !all;
                              }}
                              onChange={() =>
                                setForm({
                                  ...form,
                                  permissions: all
                                    ? form.permissions.filter((k) => !keys.includes(k))
                                    : [...new Set([...form.permissions, ...keys])],
                                })
                              }
                            />
                            <strong>{moduleName(module)}</strong>
                          </label>
                          <small>
                            {selectedKeys.length}/{keys.length} izin
                          </small>
                        </div>
                        <div className="st-perm-actions">
                          {keys.map((key) => {
                            const on = form.permissions.includes(key);
                            return (
                              <label
                                key={key}
                                className="st-perm-chip"
                                data-on={on}
                                title={permissionHint(key) ?? key}
                              >
                                <input
                                  type="checkbox"
                                  checked={on}
                                  onChange={() =>
                                    setForm({
                                      ...form,
                                      permissions: on
                                        ? form.permissions.filter((k) => k !== key)
                                        : [...form.permissions, key],
                                    })
                                  }
                                />
                                {actionLabel(key)}
                              </label>
                            );
                          })}
                        </div>
                      </div>
                    );
                  })}
                </div>
              )}
              <small className="st-hint">
                Departman rolün izin verdiğini <strong>daraltır</strong>, genişletemez: buraya
                eklenen bir izin, rolü yetmeyen kullanıcıya açılmaz. Yöneticiler bu kısıttan
                muaftır. Hiçbir departmana atanmamış kullanıcı rolünün varsayılanlarını görür.
                Panele giriş departmandan etkilenmez — role bağlıdır.
              </small>
            </fieldset>
            <div style={{ display: 'flex', gap: 10 }}>
              <button type="submit" className="st-btn" disabled={busy}>
                {busy ? 'Kaydediliyor…' : editingId ? 'Kaydet' : '+ Ekle'}
              </button>
              <button
                type="button"
                className="st-btn st-btn--ghost"
                onClick={() => {
                  setFormOpen(false);
                  setEditingId(null);
                }}
              >
                Vazgeç
              </button>
            </div>
          </form>
        </section>
      )}

      {!loading && companyId && (
        <section className="st-card">
          <div className="st-card-head">
            <div>
              <h2>Departmanlar</h2>
              <p className="st-card-sub">
                Her departmanın izinleri modül ve aksiyon kırılımında; rolün verdiğini daraltır.
              </p>
            </div>
            <span className="st-head-meta">{departments.length} departman</span>
          </div>

          {departments.length === 0 ? (
            <p className="st-empty">
              Henüz departman tanımlanmadı. “Departman ekle” ile başlayabilirsiniz.
            </p>
          ) : (
            <div className="st-table-wrap">
              <table className="st-table">
                <thead>
                  <tr>
                    <th>Ad</th>
                    <th>Kod</th>
                    <th>Yönetici</th>
                    <th>İzinler</th>
                    <th>Üye</th>
                    <th className="st-right">İşlem</th>
                  </tr>
                </thead>
                <tbody>
                  {departments.map((d) => (
                    <tr key={d.id}>
                      <td className="st-strong">{d.name}</td>
                      <td className="st-mono st-dim">{d.code || '—'}</td>
                      <td className="st-dim">
                        {d.managerKeycloakUserId ? userLabel(d.managerKeycloakUserId) : '—'}
                      </td>
                      <td className="st-dim">
                        {(d.permissions ?? []).length === 0 ? (
                          // Bos liste "kisit yok" degil "hicbir izin" demek;
                          // yanlis okunmasin diye acikca yaziliyor.
                          <span className="st-badge st-badge--warn">izin verilmedi</span>
                        ) : (
                          // Modul basina "2/3" kirilimi: satira yirmi bes izin
                          // anahtari sigmiyor, ama "hangi modulde ne kadar
                          // yetki var" sorusu tablodan okunabilmeli.
                          <span className="st-module-tags">
                            {(d.modules ?? []).map((m) => {
                              const total =
                                actionCatalog.find((a) => a.module === m)?.permissions.length ?? 0;
                              const own = (d.permissions ?? []).filter((p) =>
                                p.startsWith(`${m}.`)
                              ).length;
                              return (
                                <span key={m} className="st-chip">
                                  {moduleName(m)}
                                  {total > 0 ? ` ${own}/${total}` : ''}
                                </span>
                              );
                            })}
                          </span>
                        )}
                      </td>
                      <td className="st-mono st-dim">{d.memberCount}</td>
                      <td className="st-right">
                        <button
                          type="button"
                          className="st-link"
                          onClick={() => setSelectedDepartment(d)}
                        >
                          Üyeler
                        </button>{' '}
                        {canUpdate && (
                          <>
                            <button type="button" className="st-link" onClick={() => startEdit(d)}>
                              Düzenle
                            </button>{' '}
                          </>
                        )}
                        {canDelete && (
                          <button type="button" className="st-link" onClick={() => remove(d)}>
                            Sil
                          </button>
                        )}
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          )}
        </section>
      )}

      {!loading && selectedDepartment && (
        <MembersPanel
          token={token}
          department={selectedDepartment}
          canAssign={canAssign}
          companyUsers={companyUsers}
          userLabel={userLabel}
          onClose={() => setSelectedDepartment(null)}
          onChanged={load}
          setError={setError}
          setNotice={setNotice}
        />
      )}
    </div>
  );
}

function MembersPanel({
  token,
  department,
  canAssign,
  companyUsers,
  userLabel,
  onClose,
  onChanged,
  setError,
  setNotice,
}) {
  const [members, setMembers] = useState([]);
  const [loading, setLoading] = useState(true);
  const [busy, setBusy] = useState(false);
  const [pick, setPick] = useState('');

  const load = useCallback(async () => {
    setLoading(true);
    try {
      setMembers(await fetchDepartmentMembers(token, department.id));
    } catch (err) {
      setError(describeError(err, 'Departman üyeleri okunamadı.'));
    } finally {
      setLoading(false);
    }
  }, [token, department.id, setError]);

  useEffect(() => {
    load();
  }, [load]);

  const add = async () => {
    if (!pick) return;
    setBusy(true);
    setError('');
    setNotice('');
    try {
      await assignUserToDepartment(token, department.id, pick);
      setPick('');
      setNotice('Kullanıcı departmana eklendi.');
      await Promise.all([load(), onChanged()]);
    } catch (err) {
      setError(describeError(err, 'Kullanıcı eklenemedi.'));
    } finally {
      setBusy(false);
    }
  };

  const remove = async (keycloakUserId) => {
    setError('');
    setNotice('');
    try {
      await removeUserFromDepartment(token, department.id, keycloakUserId);
      setNotice('Kullanıcı departmandan çıkarıldı.');
      await Promise.all([load(), onChanged()]);
    } catch (err) {
      setError(describeError(err, 'Kullanıcı çıkarılamadı.'));
    }
  };

  const memberIds = new Set(members.map((m) => m.keycloakUserId));
  const assignable = companyUsers.filter((u) => !memberIds.has(u.keycloakUserId));

  return (
    <section className="st-card">
      <div className="st-card-head">
        <div>
          <h2>{department.name} · üyeler</h2>
          <p className="st-card-sub">
            Bir kişi birden fazla departmanda olabilir. Departmana yalnızca bu şirketin üyeleri
            atanabilir.
          </p>
        </div>
        <button type="button" className="st-link" onClick={onClose}>
          Kapat
        </button>
      </div>

      {loading && <p className="st-empty">Yükleniyor…</p>}

      {!loading && members.length === 0 && <p className="st-empty">Bu departmanda üye yok.</p>}

      {!loading && members.length > 0 && (
        <div className="st-table-wrap">
          <table className="st-table">
            <thead>
              <tr>
                <th>Kullanıcı</th>
                <th>Eklenme</th>
                <th className="st-right">İşlem</th>
              </tr>
            </thead>
            <tbody>
              {members.map((m) => (
                <tr key={m.id}>
                  <td className="st-strong">{userLabel(m.keycloakUserId)}</td>
                  <td className="st-mono st-dim">
                    {new Date(m.assignedAt).toLocaleDateString('tr-TR')}
                  </td>
                  <td className="st-right">
                    {canAssign && (
                      <button
                        type="button"
                        className="st-link"
                        onClick={() => remove(m.keycloakUserId)}
                      >
                        Çıkar
                      </button>
                    )}
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}

      {/* Üye atama ayrı bir izin: departmanı görebilen herkes kimin hangi
          departmanda olduğunu değiştirebilmemeli. */}
      <div
        className="st-form-grid"
        style={{ marginTop: 18, alignItems: 'end', display: canAssign ? undefined : 'none' }}
      >
        <label className="st-field">
          <span>Kullanıcı ekle</span>
          <select value={pick} onChange={(e) => setPick(e.target.value)}>
            <option value="">Seçin…</option>
            {assignable.map((u) => (
              <option key={u.keycloakUserId} value={u.keycloakUserId}>
                {userLabel(u.keycloakUserId)} · {roleName(u.role)}
              </option>
            ))}
          </select>
        </label>
        <button
          type="button"
          className="st-btn st-btn--sm"
          disabled={!pick || busy}
          onClick={add}
          style={{ alignSelf: 'end', marginBottom: 2 }}
        >
          {busy ? 'Ekleniyor…' : 'Ekle'}
        </button>
      </div>

      {assignable.length === 0 && !loading && (
        <p className="st-hint" style={{ marginTop: 10 }}>
          Eklenebilecek başka kullanıcı yok — önce Kullanıcılar sayfasından şirkete ekleyin.
        </p>
      )}
    </section>
  );
}
