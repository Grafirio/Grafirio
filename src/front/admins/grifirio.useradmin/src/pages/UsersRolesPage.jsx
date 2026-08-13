import { useSearchParams } from 'react-router-dom';
import UsersPage from './settings/UsersPage';
import RolesPage from './settings/RolesPage';
import '../styles/SettingsPages.css';

// Onceden "Kullanici Ayarlari" (/settings/user) ve "Yetki Ayarlari"
// (/settings/authorization) ayri menu girdileriydi, ustelik Sirket
// Bilgileri'ndeki "Yetkili kullanicilar" sekmesi de ayni listeyi ucuncu kez
// gosteriyordu. Uc baslik da "kullanicilar" sorusuna bakiyordu; artik tek
// sayfa, iki sekme: kim ne yapabilir (roller, salt-okunur matris) ve kim var
// (kullanicilar, gercek CRUD).
const TABS = [
  { key: 'users', label: 'Kullanıcılar' },
  { key: 'roles', label: 'İzinler ve roller' },
];

export default function UsersRolesPage() {
  const [params, setParams] = useSearchParams();
  const tab = TABS.some((t) => t.key === params.get('tab')) ? params.get('tab') : 'users';

  return (
    <div className="st">
      <div className="st-head">
        <div>
          <p className="st-eyebrow">Ayarlar · Kullanıcılar</p>
          <h1>Kullanıcılar</h1>
          <p className="st-lead">
            Şirketinizdeki kullanıcılar, rolleri ve rollerin neye izin verdiği tek yerde.
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
            onClick={() => setParams(t.key === 'users' ? {} : { tab: t.key })}
          >
            {t.label}
          </button>
        ))}
      </div>

      {tab === 'users' && <UsersPage embedded />}
      {tab === 'roles' && <RolesPage embedded />}
    </div>
  );
}
