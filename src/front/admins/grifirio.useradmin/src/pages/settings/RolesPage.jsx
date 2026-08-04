import { useEffect, useState } from 'react';
import { useNavigate } from 'react-router-dom';
import { useKeycloak } from '@react-keycloak/web';
import {
  COMPANY_ROLES,
  describeError,
  fetchCompanyUsers,
} from '../../services/companyService';
import '../../styles/SettingsPages.css';

/**
 * Ayarlar › Yetki Ayarlari.
 *
 * Roller uydurma degil: Identity'deki CompanyRoles sabitinin ta kendisi.
 * Asagidaki izin matrisi de oyle — her satir, sunucuda gercekten role
 * bakan bir kontrolden geliyor (AssignRole ve RevokeRole yalnizca
 * COMPANY_ADMIN, kullanici kaydi COMPANY_ADMIN ya da COMPANY_MANAGER,
 * abonelik baslatma COMPANY_ADMIN, sirket olusturma COMPANY_ADMIN).
 *
 * Panelin geri kalani role degil aboneli bakiyor; o satirlar bu yuzden uc
 * rolde de acik. Matrise yeni satir eklenecekse once sunucuda karsiligi
 * olmali, yoksa ekranda yazan sey ile sistemin yaptigi sey ayrisir.
 */

const A = '✓';
const N = '—';

const PERMISSIONS = [
  { name: 'Panele ve kanvaslara erişim', admin: A, manager: A, user: A, note: 'abonelik kontrolü' },
  { name: 'Veri kaynağı bağlama ve analiz başlatma', admin: A, manager: A, user: A },
  { name: 'AI sorgulama çalıştırma', admin: A, manager: A, user: A },
  { name: 'Kullanıcı kaydı oluşturma', admin: A, manager: A, user: N },
  { name: 'Rol atama', admin: A, manager: N, user: N },
  { name: 'Kullanıcı erişimini kaldırma', admin: A, manager: N, user: N },
  { name: 'Alt şirket oluşturma', admin: A, manager: N, user: N },
  { name: 'Abonelik başlatma', admin: A, manager: N, user: N },
];

const ROLE_ACCENT = {
  COMPANY_ADMIN: 'var(--gf-navy)',
  COMPANY_MANAGER: 'var(--gf-teal)',
  COMPANY_USER: 'var(--gf-sun)',
};

export default function RolesPage() {
  const navigate = useNavigate();
  const { keycloak } = useKeycloak();
  const token = keycloak.token;
  const companyId = keycloak.tokenParsed?.company_id;

  const [users, setUsers] = useState([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState('');

  useEffect(() => {
    if (!companyId) {
      setLoading(false);
      return undefined;
    }
    let cancelled = false;
    (async () => {
      try {
        const members = await fetchCompanyUsers(token, companyId);
        if (!cancelled) setUsers(members);
      } catch (err) {
        if (!cancelled) setError(describeError(err, 'Kullanıcı sayıları okunamadı.'));
      } finally {
        if (!cancelled) setLoading(false);
      }
    })();
    return () => {
      cancelled = true;
    };
  }, [token, companyId]);

  const countFor = (code) => users.filter((u) => u.role === code).length;

  return (
    <div className="st">
      <div className="st-head">
        <div>
          <p className="st-eyebrow">Ayarlar · Güvenlik</p>
          <h1>Yetki Ayarları</h1>
          <p className="st-lead">
            Roller ve izinler. Bir kullanıcının şirket içindeki rolü, hangi yönetim işlemlerini
            yapabileceğini belirler.
          </p>
        </div>
        <div className="st-head-actions">
          <button type="button" className="st-btn" onClick={() => navigate('/settings/user')}>
            Kullanıcı rollerini yönet
          </button>
        </div>
      </div>

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
              Sunucunun şu an uyguladığı kurallar. Roller sabittir; yeni rol tanımlamak Identity
              tarafında değişiklik gerektirir.
            </p>
          </div>
        </div>

        <div className="st-table-wrap">
          <table className="st-table st-matrix">
            <thead>
              <tr>
                <th>İzin</th>
                <th>Yönetici</th>
                <th>Müdür</th>
                <th>Kullanıcı</th>
              </tr>
            </thead>
            <tbody>
              {PERMISSIONS.map((p) => (
                <tr key={p.name}>
                  <td>
                    {p.name}
                    {p.note && (
                      <span className="st-mono st-dim" style={{ display: 'block', fontSize: 11.5 }}>
                        {p.note}
                      </span>
                    )}
                  </td>
                  <td style={{ color: p.admin === N ? 'var(--gf-fainter)' : undefined }}>{p.admin}</td>
                  <td style={{ color: p.manager === N ? 'var(--gf-fainter)' : undefined }}>{p.manager}</td>
                  <td style={{ color: p.user === N ? 'var(--gf-fainter)' : undefined }}>{p.user}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>

        <div className="st-legend">
          <span>✓ izinli</span>
          <span>— izinsiz</span>
        </div>
      </section>
    </div>
  );
}
