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

// Veri kaynagi islerinin hepsi kendi basligi altinda: veriyi nereden
// aldigimiz (SQL baglantisi) ile elle veri girisi ayni ise ait, ayarlarin
// icinde kaybolmalari kullaniciyi bagliyordu.
const CONNECTION_MENU = [
  { to: '/settings/data-input', label: 'Veri Girdisi' },
  { to: '/settings/sql-connection', label: 'SQL Bağlantı Ayarları' },
];

// Sirket profili ve alt sirketler menuden cikarildi; ikisi de Sirket
// Ayarlari sayfasinin icinde yasayacak. Ayrica "Kullanici Ayarlari" ile
// "Yetkili kullanicilar" ayni ekrani aciyordu, tek baslik birakildi.
const SETTINGS_MENU = [
  { to: '/settings/company', label: 'Şirket Ayarları' },
  { to: '/company-info?tab=users', label: 'Yetkili Kullanıcılar' },
  { divider: true },
  { to: '/settings/department', label: 'Departman Ayarları' },
  { to: '/settings/authorization', label: 'Yetki Ayarları' },
  { divider: true },
  { to: '/settings/membership', label: 'Üyelik Bilgileri' },
];

const CONNECTION_PATHS = CONNECTION_MENU.map((item) => item.to);

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
  const atConnection = CONNECTION_PATHS.includes(location.pathname);
  const atSettings =
    (location.pathname.startsWith('/settings') && !atConnection) ||
    location.pathname === '/company-info';

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

          <NavDropdown label="Bağlantı Ayarları" items={CONNECTION_MENU} active={atConnection} />
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
