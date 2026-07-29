import { NavLink, Outlet } from 'react-router-dom';
import { useKeycloak } from '@react-keycloak/web';

const NAV = [
  { to: '/companies', label: 'Firmalar' },
  { to: '/services', label: 'Servisler' },
];

/** Token'daki business_roles claim'i platform ekibini isaret ediyor mu. */
export const isPlatformAdmin = (keycloak) => {
  const roles = keycloak?.tokenParsed?.business_roles;
  if (!roles) return false;
  return (Array.isArray(roles) ? roles : [roles]).includes('PLATFORM_ADMIN');
};

export default function AdminLayout() {
  const { keycloak, initialized } = useKeycloak();

  if (!initialized) {
    return (
      <div className="pa-center">
        <span className="gf-spinner gf-spinner--lg" />
      </div>
    );
  }

  if (!keycloak.authenticated) {
    return (
      <div className="pa-center">
        <div className="gf-empty">
          <h3>Oturum gerekli</h3>
          <p>Bu panele erişmek için giriş yapmalısınız.</p>
          <button className="gf-btn gf-btn--primary" onClick={() => keycloak.login()}>
            Giriş yap
          </button>
        </div>
      </div>
    );
  }

  // Panel musteri verisinin tamamini yonetiyor; firma kapsamli rollerle
  // acilmamali. Sunucu tarafi da ayrica kontrol ediyor, bu yalnizca arayuzu
  // yanlis kisiye gostermemek icin.
  if (!isPlatformAdmin(keycloak)) {
    return (
      <div className="pa-center">
        <div className="gf-empty">
          <h3>Bu panele erişim yetkiniz yok</h3>
          <p>
            ProjectAdmin yalnızca platform ekibine açıktır. Ürünü kullanmak için
            UserAdmin üzerinden devam edin.
          </p>
          <button className="gf-btn" onClick={() => keycloak.logout()}>
            Çıkış yap
          </button>
        </div>
      </div>
    );
  }

  return (
    <div className="pa-shell">
      <header className="pa-header">
        <div className="pa-brand">Grafirio · ProjectAdmin</div>
        <nav className="pa-nav">
          {NAV.map(({ to, label }) => (
            <NavLink
              key={to}
              to={to}
              className={({ isActive }) => `pa-nav__link${isActive ? ' is-active' : ''}`}
            >
              {label}
            </NavLink>
          ))}
        </nav>
        <div className="pa-user">
          <span className="gf-muted gf-text-sm">
            {keycloak.tokenParsed?.preferred_username}
          </span>
          <button className="gf-btn gf-btn--sm gf-btn--ghost" onClick={() => keycloak.logout()}>
            Çıkış
          </button>
        </div>
      </header>
      <main className="pa-main">
        <Outlet />
      </main>
    </div>
  );
}
