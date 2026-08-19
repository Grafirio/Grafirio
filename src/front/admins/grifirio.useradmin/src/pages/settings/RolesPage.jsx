import { useCallback, useEffect, useState } from 'react';
import { useNavigate } from 'react-router-dom';
import { useKeycloak } from '@react-keycloak/web';
import { describeError, fetchPermissionActions } from '../../services/companyService';
import {
  createRole,
  deleteRole,
  fetchRoles,
  updateRole,
} from '../../services/roleService';
import { useCompany } from '../../contexts/companyContext';
import { moduleName } from '../../constants/modules';
import { moduleOf } from '../../constants/permissions';
import PermissionMatrix from '../../components/organisms/PermissionMatrix';
import '../../styles/SettingsPages.css';

const emptyForm = () => ({ name: '', description: '', permissions: [] });

/**
 * Ayarlar › Roller.
 *
 * Rol, şirketin kendi adlandırdığı bir izin kümesi — "Muhasebe", "Saha". Sabit
 * bir merdiven değil: kişinin etkin izni, taşıdığı rollerin ve kişisel
 * izinlerinin birleşimi. Kime hangi rolün verildiği Kullanıcılar ekranında.
 *
 * Roller şirkete bağlı. Hangi şirketin rollerine bakıldığı Nav'daki şirket
 * değiştiriciden geliyor; aynı kişi bir şirkette muhasebeci, diğerinde
 * finansçı olabilsin diye her şirketin kendi şeması var.
 */
export default function RolesPage() {
  const navigate = useNavigate();
  const { keycloak } = useKeycloak();
  const token = keycloak.token;
  const { selected: company, can } = useCompany();
  const companyId = company?.id;

  const canCreate = can('ROLES.CREATE');
  const canUpdate = can('ROLES.UPDATE');
  const canDelete = can('ROLES.DELETE');
  // İzin kümesini düzenlemek rolü yeniden adlandırmaktan ayrı bir yetki.
  const canManagePermissions = can('ROLES.MANAGE_PERMISSIONS');

  const [roles, setRoles] = useState([]);
  const [catalog, setCatalog] = useState([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState('');
  const [notice, setNotice] = useState('');

  const [editingId, setEditingId] = useState(null);
  const [form, setForm] = useState(emptyForm());
  const [formOpen, setFormOpen] = useState(false);
  const [busy, setBusy] = useState(false);

  const load = useCallback(async () => {
    if (!companyId) {
      setLoading(false);
      return;
    }
    setLoading(true);
    setError('');
    try {
      const [list, actions] = await Promise.all([
        fetchRoles(token, companyId),
        // Matris okunamazsa sayfa yine açılıyor: rol adları ve üye sayıları
        // görünür kalsın, kullanıcı boş ekranla karşılaşmasın.
        fetchPermissionActions(token).catch(() => []),
      ]);
      setRoles(list);
      setCatalog(actions);
    } catch (err) {
      setError(describeError(err, 'Roller okunamadı.'));
    } finally {
      setLoading(false);
    }
  }, [token, companyId]);

  useEffect(() => {
    load();
    setFormOpen(false);
    setEditingId(null);
  }, [load]);

  const startCreate = () => {
    setEditingId(null);
    setForm(emptyForm());
    setFormOpen(true);
  };

  const startEdit = (role) => {
    setEditingId(role.id);
    setForm({
      name: role.name ?? '',
      description: role.description ?? '',
      permissions: role.permissions ?? [],
    });
    setFormOpen(true);
  };

  const submit = async (e) => {
    e.preventDefault();
    if (!form.name.trim()) {
      setError('Rol adı gerekli.');
      return;
    }
    setBusy(true);
    setError('');
    setNotice('');
    try {
      const payload = {
        name: form.name.trim(),
        description: form.description.trim() || null,
        permissions: form.permissions,
      };

      if (editingId) {
        await updateRole(token, editingId, payload);
        setNotice('Rol güncellendi.');
      } else {
        await createRole(token, { ...payload, companyId });
        setNotice('Rol eklendi.');
      }

      setFormOpen(false);
      setEditingId(null);
      setForm(emptyForm());
      await load();
    } catch (err) {
      setError(describeError(err, 'Rol kaydedilemedi.'));
    } finally {
      setBusy(false);
    }
  };

  const remove = async (role) => {
    setError('');
    setNotice('');
    try {
      await deleteRole(token, role.id);
      setNotice('Rol silindi.');
      await load();
    } catch (err) {
      setError(describeError(err, 'Rol silinemedi.'));
    }
  };

  return (
    <div className="st">
      <div className="st-head">
        <div>
          <button type="button" className="st-back" onClick={() => navigate('/settings')}>
            ← Ayarlar
          </button>
          <h1>Roller</h1>
          <p className="st-lead">
            {company
              ? `${company.name} için tanımlı izin kümeleri.`
              : 'Şirketin kendi adlandırdığı izin kümeleri.'}
          </p>
        </div>
        {!loading && companyId && canCreate && (
          <div className="st-head-actions">
            <button type="button" className="st-btn" onClick={startCreate}>
              + Rol ekle
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
          <p className="st-caps">{editingId ? 'Rolü düzenle' : 'Yeni rol'}</p>
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
                <small className="st-hint">Şirket içinde benzersiz olmalı.</small>
              </label>
              <label className="st-field">
                <span>Açıklama</span>
                <input
                  value={form.description}
                  onChange={(e) => setForm({ ...form, description: e.target.value })}
                  placeholder="Fatura ve belge işleri"
                />
              </label>
            </div>

            <fieldset className="st-field" style={{ border: 0, margin: 0, padding: 0 }}>
              <span>İzinler</span>
              <PermissionMatrix
                catalog={catalog}
                value={form.permissions}
                onChange={
                  canManagePermissions
                    ? (permissions) => setForm({ ...form, permissions })
                    : undefined
                }
                emptyText="İzin listesi okunamadı; kaydedilen izinler korunuyor."
              />
              <small className="st-hint">
                {canManagePermissions ? (
                  <>
                    Rol izin <strong>verir</strong>, daraltmaz: bir kişi birden fazla rol
                    taşıyabilir ve etkin izni bunların birleşimidir. Kurucu ve adminler bu
                    kümenin dışında — her şeye erişirler.
                  </>
                ) : (
                  <>
                    İzin kümesini değiştirmek ayrı bir yetki gerektiriyor; rolün diğer
                    alanlarını düzenleyebilirsiniz.
                  </>
                )}
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
              <h2>Tanımlı roller</h2>
              <p className="st-card-sub">
                Kime hangi rolün verildiği Kullanıcılar ekranında; oradan kişiye özel izin de
                verilebiliyor.
              </p>
            </div>
            <span className="st-head-meta">{roles.length} rol</span>
          </div>

          {roles.length === 0 ? (
            <p className="st-empty">
              Henüz rol tanımlanmadı. “Rol ekle” ile başlayabilirsiniz.
            </p>
          ) : (
            <div className="st-table-wrap">
              <table className="st-table">
                <thead>
                  <tr>
                    <th>Ad</th>
                    <th>İzinler</th>
                    <th>Kişi</th>
                    <th className="st-right">İşlem</th>
                  </tr>
                </thead>
                <tbody>
                  {roles.map((role) => {
                    const permissions = role.permissions ?? [];
                    const byModule = [...new Set(permissions.map(moduleOf))];

                    return (
                      <tr key={role.id}>
                        <td className="st-strong">
                          {role.name}
                          {role.description && (
                            <span className="st-doc-meta"> · {role.description}</span>
                          )}
                        </td>
                        <td className="st-dim">
                          {permissions.length === 0 ? (
                            // Bos liste "kisit yok" degil "hicbir izin" demek.
                            <span className="st-badge st-badge--warn">izin verilmedi</span>
                          ) : (
                            <span className="st-module-tags">
                              {byModule.map((m) => (
                                <span
                                  key={m}
                                  className="st-chip"
                                  title={permissions.filter((p) => moduleOf(p) === m).join(', ')}
                                >
                                  {moduleName(m)}
                                  <b className="st-chip-count">
                                    {permissions.filter((p) => moduleOf(p) === m).length}
                                  </b>
                                </span>
                              ))}
                            </span>
                          )}
                        </td>
                        <td className="st-mono st-dim">{role.memberCount}</td>
                        <td className="st-right">
                          {canUpdate && (
                            <>
                              <button
                                type="button"
                                className="st-link"
                                onClick={() => startEdit(role)}
                              >
                                Düzenle
                              </button>{' '}
                            </>
                          )}
                          {canDelete && (
                            <button
                              type="button"
                              className="st-link"
                              onClick={() => remove(role)}
                            >
                              Sil
                            </button>
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
    </div>
  );
}
