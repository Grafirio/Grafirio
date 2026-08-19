import { useEffect, useState } from 'react';
import { useNavigate } from 'react-router-dom';
import { useKeycloak } from '@react-keycloak/web';
import {
  COMPANY_ROLES,
  describeError,
  fetchCompanyUsers,
  fetchPermissionActions,
  fetchRolePermissions,
} from '../../services/companyService';
import { useCompany } from '../../contexts/companyContext';
import { moduleName } from '../../constants/modules';
import { actionLabel, permissionHint } from '../../constants/permissions';
import '../../styles/SettingsPages.css';

/**
 * Ayarlar › İzinler › Rol izinleri.
 *
 * Matris artık elle yazılmıyor: satırlar sunucudan gelen izin anahtarları
 * (GET /permissions/actions), hücreler de rol tavanları (GET /permissions/roles).
 * Önceden tablo bu dosyada sabitti ve kendi yorumu "yeni satır eklenecekse önce
 * sunucuda karşılığı olmalı" diye uyarıyordu — ekranda yazan izinle sistemin
 * uyguladığı izin ayrışabiliyordu. Kaynak tek: AppPermissions.
 *
 * Tablo salt okunur. Rol tavanını değiştiren bir uç yok; rol başına izin
 * düzenlemek yerine daraltma departman üzerinden yapılıyor.
 *
 * embedded: İzinler sayfası bu bileşeni "Rol izinleri" sekmesinde gösterir.
 */
const ROLE_ACCENT = {
  COMPANY_ADMIN: 'var(--gf-navy)',
  COMPANY_MANAGER: 'var(--gf-teal)',
  COMPANY_USER: 'var(--gf-sun)',
};

/** Tablo kolonları: platform ekibi burada gösterilmiyor, müşteri rolü değil. */
const COLUMNS = ['COMPANY_ADMIN', 'COMPANY_MANAGER', 'COMPANY_USER'];

export default function RolesPage({ embedded = false } = {}) {
  const navigate = useNavigate();
  const { keycloak } = useKeycloak();
  const token = keycloak.token;
  // Şirket seçiciden: aynı kullanıcı bir şubede yönetici, başka birinde
  // sıradan kullanıcı olabiliyor ve kullanıcı sayıları da şubeye göre değişir.
  const { selected: company } = useCompany();
  const companyId = company?.id;

  const [users, setUsers] = useState([]);
  const [catalog, setCatalog] = useState([]);
  const [ceilings, setCeilings] = useState({});
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState('');

  useEffect(() => {
    let cancelled = false;
    (async () => {
      setLoading(true);
      try {
        const [actions, roles, members] = await Promise.all([
          fetchPermissionActions(token),
          fetchRolePermissions(token),
          // Kullanıcı sayısı tablonun yanında bir bilgi; okunamazsa matris
          // yine gösterilmeli.
          companyId ? fetchCompanyUsers(token, companyId).catch(() => []) : Promise.resolve([]),
        ]);
        if (cancelled) return;
        setCatalog(actions);
        setCeilings(Object.fromEntries(roles.map((r) => [r.role, new Set(r.permissions)])));
        setUsers(members);
      } catch (err) {
        if (!cancelled) setError(describeError(err, 'İzin tabloları okunamadı.'));
      } finally {
        if (!cancelled) setLoading(false);
      }
    })();
    return () => {
      cancelled = true;
    };
  }, [token, companyId]);

  const countFor = (code) => users.filter((u) => u.role === code).length;
  const has = (role, permission) => ceilings[role]?.has(permission) ?? false;

  return (
    <div className={embedded ? 'st-embed' : 'st'}>
      {!embedded && (
        <div className="st-head">
          <div>
            <p className="st-eyebrow">Ayarlar · Güvenlik</p>
            <h1>Rol izinleri</h1>
            <p className="st-lead">
              Bir kullanıcının şirket içindeki rolü, hangi işlemleri yapabileceğinin üst sınırını
              belirler.
            </p>
          </div>
          <div className="st-head-actions">
            <button type="button" className="st-btn" onClick={() => navigate('/settings/users')}>
              Kullanıcı rollerini yönet
            </button>
          </div>
        </div>
      )}

      {error && <div className="st-alert">{error}</div>}

      <div className="st-grid-3">
        {COMPANY_ROLES.map((r) => (
          <div className="st-kpi" key={r.code} style={{ '--accent-line': ROLE_ACCENT[r.code] }}>
            <span className="st-kpi-label">{r.code}</span>
            <strong style={{ fontSize: 20 }}>{r.name}</strong>
            <p className="st-kpi-note" style={{ fontWeight: 500, color: 'var(--gf-muted)' }}>
              {r.description}
            </p>
            <p className="st-mono st-dim" style={{ marginTop: 12 }}>
              {loading ? '—' : `${countFor(r.code)} kullanıcı`}
            </p>
          </div>
        ))}
      </div>

      <section className="st-card">
        <div className="st-card-head">
          <div>
            <h2>İzin matrisi</h2>
            <p className="st-card-sub">
              Sunucunun şu an uyguladığı rol tavanları — doğrudan izin tanımından okunuyor.
              Anahtarlar tıklanamaz: rolün tavanı sabit, daraltma departman izinleriyle yapılıyor.
            </p>
          </div>
        </div>

        {loading && <p className="st-empty">Yükleniyor…</p>}

        {!loading && catalog.length === 0 && (
          <p className="st-empty">İzin listesi okunamadı.</p>
        )}

        {!loading && catalog.length > 0 && (
          <div className="st-table-wrap">
            <table className="st-table">
              <thead>
                <tr>
                  <th>İzin</th>
                  <th style={{ textAlign: 'center' }}>Yönetici</th>
                  <th style={{ textAlign: 'center' }}>Müdür</th>
                  <th style={{ textAlign: 'center' }}>Kullanıcı</th>
                </tr>
              </thead>
              {/* Modül başına ayrı tbody: yirmi beş satırı tek blokta okumak
                  zor, modül adı satırları arada başlık gibi duruyor. */}
              {catalog.map(({ module, permissions }) => (
                <tbody key={module} className="st-role-group">
                  <tr>
                    <th colSpan={4}>{moduleName(module)}</th>
                  </tr>
                  {permissions.map((permission) => (
                    <tr key={permission}>
                      <td className="st-strong">
                        {actionLabel(permission)}
                        <span
                          className="st-mono st-dim"
                          style={{ display: 'block', fontSize: 11.5, fontWeight: 500 }}
                        >
                          {permissionHint(permission) ?? permission}
                        </span>
                      </td>
                      {COLUMNS.map((role) => (
                        <td key={role} style={{ textAlign: 'center' }}>
                          <span
                            className="st-perm-toggle"
                            data-on={has(role, permission)}
                            data-locked="true"
                            title="Salt okunur — rol tavanı sunucuda sabit"
                          >
                            <i />
                          </span>
                        </td>
                      ))}
                    </tr>
                  ))}
                </tbody>
              ))}
            </table>
          </div>
        )}
      </section>
    </div>
  );
}
