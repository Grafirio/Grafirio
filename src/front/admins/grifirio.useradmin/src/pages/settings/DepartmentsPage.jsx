import { useCallback, useEffect, useState } from 'react';
import { useKeycloak } from '@react-keycloak/web';
import { describeError, fetchCompanyUsers, roleName } from '../../services/companyService';
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
import { MODULES, moduleName } from '../../constants/modules';
import '../../styles/SettingsPages.css';

const emptyForm = () => ({
  name: '',
  code: '',
  description: '',
  managerKeycloakUserId: '',
  costCenter: '',
  modules: [],
});

/**
 * Ayarlar › Departmanlar.
 *
 * Departman şirkete (şubeye) bağlı: hangi şirketin departmanlarına bakıldığı
 * Nav'daki şirket değiştiriciden geliyor. Şubeler arası geçiş yetkisini
 * departman değil şirket üyeliği belirliyor — iki kavram karışırsa "hangi
 * şirkete girebilirim" sorusunun iki ayrı cevabı olur.
 *
 * Bu aşamada departman yalnızca gruplama. Veri ve modül izinleri sonraki
 * fazlarda buraya bağlanacak.
 */
export default function DepartmentsPage() {
  const { keycloak } = useKeycloak();
  const token = keycloak.token;
  const { selected: company } = useCompany();
  const companyId = company?.id;

  const [departments, setDepartments] = useState([]);
  const [companyUsers, setCompanyUsers] = useState([]);
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
      const [list, users] = await Promise.all([
        fetchDepartments(token, companyId),
        // Üye ekleme listesi buradan: departmana yalnızca şirketin üyeleri
        // atanabiliyor.
        fetchCompanyUsers(token, companyId).catch(() => []),
      ]);
      setDepartments(list);
      setCompanyUsers(users);
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
      modules: d.modules ?? [],
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
        modules: form.modules,
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
    // Uç yalnızca kimlik döndürüyor; ad/e-posta Keycloak tarafında kalıyor.
    return user?.userName || user?.email || keycloakUserId;
  };

  return (
    <div className="st">
      <div className="st-head">
        <div>
          <p className="st-eyebrow">Ayarlar · Organizasyon</p>
          <h1>Departmanlar</h1>
          <p className="st-lead">
            {company
              ? `${company.name} altındaki organizasyon birimleri.`
              : 'Departmanlar, veri erişimi ve rapor dağıtımının temelidir.'}
          </p>
        </div>
        {!loading && companyId && (
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
              <span>Erişilebilecek modüller</span>
              <div className="st-module-grid">
                {MODULES.map((m) => {
                  const on = form.modules.includes(m.key);
                  return (
                    <label key={m.key} className="st-module-item" data-on={on}>
                      <input
                        type="checkbox"
                        checked={on}
                        onChange={() =>
                          setForm({
                            ...form,
                            modules: on
                              ? form.modules.filter((k) => k !== m.key)
                              : [...form.modules, m.key],
                          })
                        }
                      />
                      <span>
                        <strong>{m.name}</strong>
                        <small>{m.description}</small>
                      </span>
                    </label>
                  );
                })}
              </div>
              <small className="st-hint">
                Departman rolün izin verdiğini <strong>daraltır</strong>, genişletemez: buraya
                eklenen bir modül, rolü yetmeyen kullanıcıya açılmaz. Yöneticiler bu kısıttan
                muaftır. Hiçbir departmana atanmamış kullanıcı rolünün varsayılanlarını görür.
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
                Bu aşamada departman yalnızca gruplama; veri ve modül izinleri sonraki adımda
                buraya bağlanacak.
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
                    <th>Modüller</th>
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
                        {(d.modules ?? []).length === 0 ? (
                          // Bos liste "kisit yok" degil "hicbir modul" demek;
                          // yanlis okunmasin diye acikca yaziliyor.
                          <span className="st-badge st-badge--warn">modül seçilmedi</span>
                        ) : (
                          <span className="st-module-tags">
                            {d.modules.map((m) => (
                              <span key={m} className="st-chip">{moduleName(m)}</span>
                            ))}
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
                        <button type="button" className="st-link" onClick={() => startEdit(d)}>
                          Düzenle
                        </button>{' '}
                        <button type="button" className="st-link" onClick={() => remove(d)}>
                          Sil
                        </button>
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
                    <button
                      type="button"
                      className="st-link"
                      onClick={() => remove(m.keycloakUserId)}
                    >
                      Çıkar
                    </button>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}

      <div className="st-form-grid" style={{ marginTop: 18, alignItems: 'end' }}>
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
