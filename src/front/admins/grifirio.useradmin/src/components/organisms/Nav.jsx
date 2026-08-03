import { useEffect, useRef, useState } from 'react';
import { NavLink, useLocation, useNavigate } from 'react-router-dom';
import { useAuth } from '../../contexts/AuthContext';
import GMark from './GMark';
import '../../styles/Nav.css';

// Menu hiyerarsisi tasarim taslagindaki ile birebir ayni. Iki istisna,
// ikisi de comment ile isaretli: Departman/Yetki/Sirket-genel-ayarlar/Veri
// Girdisi hala placeholder ("Burasi bir ayar sayfasidir"), cunku arkalarinda
// gercek veri yok — sahte tablo doldurmak yerine oldugu gibi birakildi.
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
  // Tasarimda ayri bir "Kullanici Ayarlari" sayfasi var (davet + oturum
  // guvenligi). Davet ucu SMTP karari bekledigi icin henuz yok; bu baglanti
  // simdilik gercekten var olan tek kullanici yonetimine, company-info'nun
  // "users" sekmesine gidiyor.
  { to: '/company-info?tab=users', label: 'Kullanıcı Ayarları' },
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
          <button className="nv-user" onClick={logout} title="Çıkış yap">
            <span className="nv-avatar">{initials}</span>
            <span className="nv-user-text">
              <span className="nv-user-name">{user?.name || user?.email}</span>
              <span className="nv-user-role">çıkış yap</span>
            </span>
          </button>
        </div>
      </div>
    </header>
  );
}
