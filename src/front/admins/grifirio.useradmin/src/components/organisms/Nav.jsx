import { useEffect, useState } from 'react';
import { NavLink, useLocation, useNavigate } from 'react-router-dom';
import { useKeycloak } from '@react-keycloak/web';
import { useAuth } from '../../contexts/AuthContext';
import { useTheme } from '../../contexts/ThemeContext';
import { fetchCompanies, roleName } from '../../services/companyService';
import { bridgeInstallerUrl } from '../../services/dataAnalysisService';
import GMark from './GMark';
import '../../styles/Nav.css';

/**
 * Panel, masaüstü uygulamasının içinde de açılıyor. Orada "masaüstü
 * uygulamasını indir" düğmesi göstermek, kullanıcıya zaten çalıştırdığı şeyi
 * indirtmek olurdu.
 */
const insideDesktopApp = () => Boolean(window.__GRAFIRIO_DESKTOP__);

// Onceden iki acilir menu vardi (Baglanti Ayarlari, Ayarlar). Yeni tasarim
// bunlari ustte sekmeye ceviriyor: Ayarlar artik kendi kart-hub sayfasini
// aciyor, alt basliklari secmek icin tikla-bekle-sec akisina gerek kalmiyor.
// Uyelik ayri bir sekme: taslakta da ust seviyede, Ayarlar hub'inin icine
// gomulunce "faturami nasil gorurum" sorusu iki tikla cevaplaniyordu.
const TABS = [
  { to: '/dashboard', label: 'Dashboard', match: (p) => p === '/' || p === '/dashboard' },
  { to: '/data', label: 'Veri kaynakları', match: (p) => p.startsWith('/data') },
  {
    to: '/settings',
    label: 'Ayarlar',
    match: (p) => p.startsWith('/settings') && !p.startsWith('/settings/membership'),
  },
  { to: '/settings/membership', label: 'Üyelik', match: (p) => p.startsWith('/settings/membership') },
];

export default function Nav() {
  const { user, logout } = useAuth();
  const { keycloak } = useKeycloak();
  const { theme, toggleTheme } = useTheme();
  const location = useLocation();
  const navigate = useNavigate();

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

  // Sirket rozeti: taslakta "Enco Endustri A.S." sabit yaziyordu, biz
  // gercek sirket adini okuyoruz — yoksa rozet hic gorunmez, uydurma isim
  // konmaz.
  const companyId = keycloak.tokenParsed?.company_id;
  const [companyName, setCompanyName] = useState('');
  useEffect(() => {
    if (!companyId) return undefined;
    let cancelled = false;
    fetchCompanies(keycloak.token)
      .then((list) => {
        if (cancelled) return;
        const company = list.find((c) => c.id === companyId);
        if (company?.name) setCompanyName(company.name);
      })
      .catch(() => {});
    return () => {
      cancelled = true;
    };
  }, [companyId, keycloak.token]);

  return (
    <header className="nv">
      <div className="nv-inner">
        <NavLink to="/dashboard" className="nv-brand">
          <GMark size={28} />
          <span>GRAFIRIO</span>
        </NavLink>

        {companyName && (
          <button
            type="button"
            className="nv-company"
            onClick={() => navigate('/settings/company')}
            title="Şirket ayarları"
          >
            <span className="nv-company-mark">{companyName[0]}</span>
            {companyName}
          </button>
        )}

        <nav className="nv-tabs">
          {TABS.map((t) => {
            const active = t.match(location.pathname);
            return (
              <button
                key={t.to}
                type="button"
                className={`nv-tab ${active ? 'is-active' : ''}`}
                onClick={() => navigate(t.to)}
              >
                {t.label}
              </button>
            );
          })}
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

          <button
            type="button"
            className="nv-theme-toggle"
            onClick={toggleTheme}
            title="Açık / karanlık"
            aria-label="Temayı değiştir"
          >
            {theme === 'dark' ? '☀' : '☾'}
          </button>

          <button
            type="button"
            className="nv-theme-toggle"
            onClick={() => navigate('/settings/notifications')}
            title="Bildirimler"
            aria-label="Bildirimler"
          >
            🔔
          </button>

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
