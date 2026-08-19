import { Navigate, useNavigate, useSearchParams } from 'react-router-dom';
import UsersPage from './settings/UsersPage';
import '../styles/SettingsPages.css';

// Onceden bu sayfa iki sekmeliydi: "Kullanicilar" ve "Izinler ve roller".
// Rol matrisi artik Ayarlar › Izinler sayfasinda, departmanlarla birlikte —
// "bu kisi ne yapabilir" sorusu tek yerde. Burada kalan soru tek: kim var,
// hangi rolde, hangi firmalarda yetkili. Eski ?tab=roles baglantilari
// kirilmasin diye Izinler'e yonlendiriliyor.
export default function UsersRolesPage() {
  const navigate = useNavigate();
  const [params] = useSearchParams();

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
            Şirketinizdeki kullanıcılar, üyelik seviyeleri ve yetkileri.
          </p>
        </div>
      </div>

      <UsersPage embedded />
    </div>
  );
}
