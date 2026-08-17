import { useNavigate, useSearchParams } from 'react-router-dom';
import DepartmentsPage from './DepartmentsPage';
import RolesPage from './RolesPage';
import { useCompany } from '../../contexts/companyContext';
import '../../styles/SettingsPages.css';

/**
 * Ayarlar › İzinler.
 *
 * Yetkiyle ilgili iki ekran tek başlık altında: rolün verdiği üst sınır ve
 * departmanın onu nasıl daralttığı. Önceden rol matrisi Kullanıcılar
 * sayfasının bir sekmesi, departmanlar ise ayrı bir menü girdisiydi; ikisi
 * aynı soruyu ("bu kişi ne yapabilir") iki farklı yerden cevaplıyordu.
 * Kullanıcılar sayfası ayrı kaldı çünkü orada sorulan soru başka: kim var.
 */
const TABS = [
  { key: 'roles', label: 'Rol izinleri' },
  { key: 'departments', label: 'Departmanlar' },
];

export default function PermissionsPage() {
  const navigate = useNavigate();
  const [params, setParams] = useSearchParams();
  const { selected: company, restrictedByDepartment } = useCompany();

  const tab = TABS.some((t) => t.key === params.get('tab')) ? params.get('tab') : 'roles';

  return (
    <div className="st">
      <div className="st-head">
        <div>
          <button type="button" className="st-back" onClick={() => navigate('/settings')}>
            ← Ayarlar
          </button>
          <h1>İzinler</h1>
          <p className="st-lead">
            {company
              ? `${company.name} için rollerin verdiği üst sınır ve departmanların daralttığı izinler.`
              : 'Rollerin verdiği üst sınır ve departmanların daralttığı izinler.'}
          </p>
        </div>
      </div>

      <div className="st-tabs" role="tablist">
        {TABS.map((t) => (
          <button
            key={t.key}
            type="button"
            role="tab"
            aria-selected={tab === t.key}
            className={tab === t.key ? 'is-active' : ''}
            onClick={() => setParams(t.key === 'roles' ? {} : { tab: t.key })}
          >
            {t.label}
          </button>
        ))}
      </div>

      {/* "Neden bu menüyü göremiyorum" sorusunun cevabı: kısıt rolden mi
          departmandan mı geliyor. Sunucu bunu ayrıca bildiriyor. */}
      {restrictedByDepartment && (
        <p className="st-hint" style={{ marginBottom: 12 }}>
          Kendi izinleriniz departman atamanızla daraltılmış durumda; aşağıdaki tablo rolün
          tavanını gösterir.
        </p>
      )}

      {tab === 'roles' ? <RolesPage embedded /> : <DepartmentsPage embedded />}
    </div>
  );
}
