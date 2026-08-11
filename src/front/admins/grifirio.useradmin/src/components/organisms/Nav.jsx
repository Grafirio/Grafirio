import { useEffect, useRef, useState } from 'react';
import { NavLink, useLocation, useNavigate } from 'react-router-dom';
import { useKeycloak } from '@react-keycloak/web';
import { useAuth } from '../../contexts/AuthContext';
import { roleName } from '../../services/companyService';
import { bridgeInstallerUrl } from '../../services/dataAnalysisService';
import GMark from './GMark';
import '../../styles/Nav.css';

/**
 * Panel, masaüstü uygulamasının içinde de açılıyor. Orada "masaüstü
 * uygulamasını indir" düğmesi göstermek, kullanıcıya zaten çalıştırdığı şeyi
 * indirtmek olurdu.
 */
const insideDesktopApp = () => Boolean(window.__GRAFIRIO_DESKTOP__);

// Menu hiyerarsisi tasarim taslagindaki ile birebir ayni ve her giris kendi
// sayfasina gidiyor. Onceden "Kullanici Ayarlari" da "Yetkili kullanicilar"
// gibi /company-info?tab=users'a cikiyordu; menude iki ayri baslik ayni
// ekrani acinca kullanici hangisinin ne yaptigini anlayamiyordu.
const COMPANY_MENU = [
  { to: '/company-info?tab=info', label: 'Şirket profili' },
  { to: '/company-info?tab=users', label: 'Yetkili kullanıcılar' },
  { to: '/company-info?tab=tree', label: 'Alt şirketler' },
];

const SETTINGS_MENU = [
  { to: '/settings/company', label: 'Şirket Ayarları' },
  { to: '/settings/data-input', label: 'Veri Girdisi' },
  { to: '/settings/sql-connection', label: 'SQL Bağlantı Ayarları' },
  { divider: true },
  { to: '/settings/department', label: 'Departman Ayarları' },
  { to: '/settings/authorization', label: 'Yetki Ayarları' },
  { to: '/settings/user', label: 'Kullanıcı Ayarları' },
  { to: '/settings/membership', label: 'Üyelik Bilgileri' },
];

function useOutsideClick(ref, onOutside) {
  useEffect(() => {
    const handler = (e) => {
      if (ref.current && !ref.current.contains(e.target)) onOutside();
    };
    document.addEventListener('mousedown', handler);
    return () => document.removeEventListener('mousedown', handler);
  }, [ref, onOutside]);
}

function NavDropdown({ label, items, active }) {
  const [open, setOpen] = useState(false);
  const ref = useRef(null);
  const navigate = useNavigate();
  useOutsideClick(ref, () => setOpen(false));

  return (
    <div className="nv-item" ref={ref}>
      <button
        type="button"
        className={`nv-link ${active ? 'is-active' : ''}`}
        onClick={() => setOpen((v) => !v)}
        aria-expanded={open}
      >
        <span>
          {label} <span className="nv-caret">▾</span>
        </span>
        {active && <span className="nv-underline" />}
      </button>
      {open && (
        <div className="nv-menu" role="menu">
          {items.map((item, i) =>
            item.divider ? (
              <span className="nv-menu-divider" key={`div-${i}`} />
            ) : (
              <button
                key={item.to}
                type="button"
                className="nv-menu-item"
                onClick={() => {
                  setOpen(false);
                  navigate(item.to);
                }}
              >
                {item.label}
              </button>
            )
          )}
        </div>
      )}
    </div>
  );
}

export default function Nav() {
  const { user, logout } = useAuth();
  const { keycloak } = useKeycloak();
  const location = useLocation();

  const atDashboard = location.pathname === '/' || location.pathname === '/dashboard';
  const atCompany = location.pathname === '/company-info';
  const atSettings = location.pathname.startsWith('/settings');

  const initials = (user?.name || user?.email || '?')
    .split(/\s+/)
    .map((p) => p[0])
    .slice(0, 2)
    .join('')
    .toUpperCase();

  // Tasarimdaki kunye ad + rol gosteriyor. Rol, Identity'nin yetki
  // atadiginda Keycloak'a yazdigi business_roles niteliginden geliyor;
  // yoksa satir bos birakiliyor, uydurulmuyor.
  const businessRoles = keycloak.tokenParsed?.business_roles;
  const role = Array.isArray(businessRoles) ? businessRoles[0] : businessRoles;

  return (
    <header className="nv">
      <div className="nv-inner">
        <NavLink to="/dashboard" className="nv-brand">
          <GMark size={30} />
          <span>GRAFIRIO</span>
        </NavLink>

        <nav className="nv-links">
          <div className="nv-item">
            <NavLink to="/dashboard" className={`nv-link ${atDashboard ? 'is-active' : ''}`}>
              <span>Dashboard</span>
              {atDashboard && <span className="nv-underline" />}
            </NavLink>
          </div>

          <NavDropdown label="Şirket Bilgileri" items={COMPANY_MENU} active={atCompany} />
          <NavDropdown label="Ayarlar" items={SETTINGS_MENU} active={atSettings} />
        </nav>

        <div className="nv-right">
          {!insideDesktopApp() && (
            <a
              href={bridgeInstallerUrl}
              className="nv-desktop"
              title="Veritabanınıza kendi ağınızdan bağlanan masaüstü uygulaması"
            >
              Masaüstü uygulamayı indir
            </a>
          )}

          <div className="nv-user">
            <span className="nv-avatar">{initials}</span>
            <span className="nv-user-text">
              <span className="nv-user-name">{user?.name || user?.email}</span>
              {role && <span className="nv-user-role">{roleName(role).toLocaleLowerCase('tr')}</span>}
            </span>
          </div>
          <button type="button" className="nv-logout" onClick={logout}>
            Çıkış
          </button>
        </div>
      </div>
    </header>
  );
}
