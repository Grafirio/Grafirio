import { useEffect, useRef, useState } from 'react';
import { NavLink, useLocation, useNavigate } from 'react-router-dom';
import { useKeycloak } from '@react-keycloak/web';
import { useAuth } from '../../contexts/AuthContext';
import { useTheme } from '../../contexts/ThemeContext';
import { roleName } from '../../services/companyService';
import { useCompany } from '../../contexts/companyContext';
import { MODULE } from '../../constants/modules';
import { bridgeInstallerUrl, getSavedConnections, listAnalyses } from '../../services/dataAnalysisService';
import GMark from './GMark';
import '../../styles/Nav.css';

/**
 * Panel, masaüstü uygulamasının içinde de açılıyor. Orada "masaüstü
 * uygulamasını indir" düğmesi göstermek, kullanıcıya zaten çalıştırdığı şeyi
 * indirtmek olurdu.
 */
const insideDesktopApp = () => Boolean(window.__GRAFIRIO_DESKTOP__);

// Onceden iki acilir menu vardi (Baglanti Ayarlari, Ayarlar). Yeni tasarim
// bunlari sekmeye ceviriyor: Ayarlar artik kendi kart-hub sayfasini aciyor,
// alt basliklari secmek icin tikla-bekle-sec akisina gerek kalmiyor. Uyelik
// ayri bir sekme: Ayarlar hub'inin icine gomulunce "faturami nasil gorurum"
// sorusu iki tikla cevaplaniyordu.
// modules: sekmenin gorunmesi icin bunlardan en az birine izin gerekiyor.
// Ayarlar birden fazla modulun kapisi oldugu icin listesi genis; hub sayfasi
// kartlari ayrica kendi iznine gore suzuyor.
const TABS = [
  {
    to: '/dashboard', label: 'Analizler', countKey: 'analyses',
    match: (p) => p === '/' || p === '/dashboard',
    modules: [MODULE.ANALYSIS],
  },
  {
    to: '/data', label: 'Veri kaynakları', countKey: 'connections',
    match: (p) => p.startsWith('/data'),
    modules: [MODULE.DATA_SOURCES],
  },
  {
    to: '/settings',
    label: 'Ayarlar',
    match: (p) => p.startsWith('/settings') && !p.startsWith('/settings/membership'),
    modules: [MODULE.COMPANY_SETTINGS, MODULE.USERS_ROLES, MODULE.DEPARTMENTS, MODULE.DOCUMENTS],
  },
  {
    to: '/settings/membership', label: 'Üyelik',
    match: (p) => p.startsWith('/settings/membership'),
    modules: [MODULE.BILLING],
  },
];

export default function Nav() {
  const { user, logout } = useAuth();
  const { keycloak } = useKeycloak();
  const { theme, toggleTheme } = useTheme();
  const location = useLocation();
  const navigate = useNavigate();
  const searchRef = useRef(null);
  const [search, setSearch] = useState('');

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

  // Sirket rozeti artik salt gosterge degil, sirket degistirici: kullanici
  // birden fazla subeye erisebiliyor ve actigi alt sirkete girebilmesi
  // gerekiyor.
  //
  // Onceki hali token'daki company_id claim'ine bakiyordu ve claim gelmeyince
  // rozet bos kaliyordu. Liste artik sunucudaki uyelik kayitlarindan geliyor
  // (bkz. /companies/accessible), claim'e hic bakilmiyor.
  const { companies, selected, selectCompany, can } = useCompany();
  const visibleTabs = TABS.filter((t) => t.modules.some((m) => can(m)));
  const [switcherOpen, setSwitcherOpen] = useState(false);

  // Disari tiklayinca kapansin; menu acikken sayfanin baska yerine tiklamak
  // icin once menuyu kapatmak zorunda kalmak rahatsiz edici.
  useEffect(() => {
    if (!switcherOpen) return undefined;
    const close = () => setSwitcherOpen(false);
    window.addEventListener('click', close);
    return () => window.removeEventListener('click', close);
  }, [switcherOpen]);

  // Sekme sayaçları (4/3 gibi): taslakta sabit yaziyordu, biz gercek
  // baglanti/analiz sayisini okuyoruz. Nav sayfalar arasinda hep monte
  // kaliyor, bu yuzden tek seferlik yukleme yeterli — sayac birkac saniye
  // eskiyebilir ama uydurma bir sayi degil.
  const [counts, setCounts] = useState({ connections: null, analyses: null });
  useEffect(() => {
    let cancelled = false;
    getSavedConnections()
      .then((result) => {
        if (cancelled) return;
        const list = result?.connections ?? result?.data ?? [];
        setCounts((c) => ({ ...c, connections: list.length }));
      })
      .catch(() => {});
    listAnalyses()
      .then((result) => {
        if (cancelled) return;
        const ready = (result?.analyses ?? []).filter((a) => a.status === 'ready').length;
        setCounts((c) => ({ ...c, analyses: ready }));
      })
      .catch(() => {});
    return () => {
      cancelled = true;
    };
  }, []);

  // ⌘K / Ctrl+K: arama kutusuna odaklan. Gercek bir komut paleti degil —
  // yalniz odak kisayolu, aramanin kendisi asagida gercekten calisiyor.
  useEffect(() => {
    const onKeyDown = (e) => {
      if ((e.metaKey || e.ctrlKey) && e.key.toLowerCase() === 'k') {
        e.preventDefault();
        searchRef.current?.focus();
      }
    };
    window.addEventListener('keydown', onKeyDown);
    return () => window.removeEventListener('keydown', onKeyDown);
  }, []);

  // Arama gercek: yazip Enter'a basinca Dashboard'a gidiyor ve orada
  // baglanti/analiz adlarina gore filtreliyor (bkz. DashboardPage.jsx
  // ?q= okuma). Dekoratif bir kutu degil.
  const submitSearch = (e) => {
    e.preventDefault();
    const q = search.trim();
    navigate(q ? `/dashboard?q=${encodeURIComponent(q)}` : '/dashboard');
  };

  return (
    <header className="nv">
      <div className="nv-top">
        <NavLink to="/dashboard" className="nv-brand">
          <GMark size={28} />
          <span>GRAFIRIO</span>
        </NavLink>

        {selected && (
          <div className="nv-company-wrap" onClick={(e) => e.stopPropagation()}>
            <button
              type="button"
              className="nv-company"
              onClick={() => setSwitcherOpen((open) => !open)}
              title="Şirket değiştir"
              aria-expanded={switcherOpen}
            >
              <span className="nv-company-mark">{selected.name[0]}</span>
              {selected.name}
              <span className="nv-company-caret" aria-hidden="true">▾</span>
            </button>

            {switcherOpen && (
              <div className="nv-company-menu" role="menu">
                <p className="nv-company-menu-caps">Şirketler</p>
                {companies.map((c) => (
                  <button
                    key={c.id}
                    type="button"
                    className={`nv-company-item ${c.id === selected.id ? 'is-active' : ''}`}
                    // Alt sirketler hiyerarsideki derinliklerine gore girintili;
                    // duz bir liste subeleri ana sirketten ayirt ettirmiyordu.
                    style={{ paddingLeft: 14 + c.level * 14 }}
                    onClick={() => {
                      selectCompany(c.id);
                      setSwitcherOpen(false);
                    }}
                  >
                    <span className="nv-company-item-name">{c.name}</span>
                    {c.role && <span className="nv-company-item-role">{roleName(c.role)}</span>}
                  </button>
                ))}
                <button
                  type="button"
                  className="nv-company-item nv-company-item--link"
                  onClick={() => {
                    setSwitcherOpen(false);
                    navigate('/settings/company');
                  }}
                >
                  Şirket ayarları →
                </button>
              </div>
            )}
          </div>
        )}

        <div className="nv-right">
          {/* Arama, taslakta da sag grupta — tema/bildirim/hesap ile ayni
              hizada duruyor, solda tek basina degil. */}
          <form className="nv-search" onSubmit={submitSearch} role="search">
            <span className="nv-search-icon" aria-hidden="true" />
            <input
              ref={searchRef}
              type="search"
              placeholder="Ara veya soru sor"
              value={search}
              onChange={(e) => setSearch(e.target.value)}
            />
            <kbd>⌘K</kbd>
          </form>

          <button
            type="button"
            className="nv-icon-btn"
            onClick={toggleTheme}
            title="Açık / karanlık"
            aria-label="Temayı değiştir"
          >
            {theme === 'dark' ? '☀' : '☾'}
          </button>

          <button
            type="button"
            className="nv-icon-btn"
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

      <div className="nv-tabbar">
        <nav className="nv-tabs">
          {visibleTabs.map((t) => {
            const active = t.match(location.pathname);
            const count = t.countKey ? counts[t.countKey] : null;
            return (
              <button
                key={t.to}
                type="button"
                className={`nv-tab ${active ? 'is-active' : ''}`}
                onClick={() => navigate(t.to)}
              >
                {t.label}
                {count != null && <span className="nv-tab-count">{count}</span>}
              </button>
            );
          })}
        </nav>

        {!insideDesktopApp() && (
          <a
            href={bridgeInstallerUrl}
            className="nv-desktop"
            title="Veritabanınıza kendi ağınızdan bağlanan masaüstü uygulaması"
          >
            Masaüstü uygulaması ↓
          </a>
        )}
      </div>
    </header>
  );
}
