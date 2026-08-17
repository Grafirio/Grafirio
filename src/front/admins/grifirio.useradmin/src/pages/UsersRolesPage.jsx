import { Navigate, useNavigate, useSearchParams } from 'react-router-dom';
import UsersPage from './settings/UsersPage';
import { useCompany } from '../contexts/companyContext';
import '../styles/SettingsPages.css';

// Onceden bu sayfa iki sekmeliydi: "Kullanicilar" ve "Izinler ve roller".
// Rol matrisi artik Ayarlar › Izinler sayfasinda, departmanlarla birlikte —
// "bu kisi ne yapabilir" sorusu tek yerde. Burada kalan soru tek: kim var,
// hangi rolde, hangi firmalarda yetkili. Eski ?tab=roles baglantilari
// kirilmasin diye Izinler'e yonlendiriliyor.
export default function UsersRolesPage() {
  const navigate = useNavigate();
  const [params] = useSearchParams();
  const { can } = useCompany();

  if (params.get('tab') === 'roles') {
    return <Navigate to="/settings/permissions" replace />;
  }

  return (
    <div className="st">
      <div className="st-head">
        <div>
          <button type="button" className="st-back" onClick={() => navigate('/settings')}>
            ← Ayarlar
          </button>
          <h1>Kullanıcılar</h1>
          <p className="st-lead">
            Şirketinizdeki kullanıcılar, rolleri ve yetkili oldukları firmalar.
          </p>
        </div>
        {can('USERS_ROLES.CREATE') && (
          <div className="st-head-actions">
            <button
              type="button"
              className="st-btn"
              disabled
              title="Davet gönderme ucu henüz yok — kullanıcılar kendileri kaydolup burada role atanıyor"
            >
              Kullanıcı davet et
            </button>
          </div>
        )}
      </div>

      <UsersPage embedded />
    </div>
  );
}
