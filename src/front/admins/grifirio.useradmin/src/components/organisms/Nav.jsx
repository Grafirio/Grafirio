import { useEffect, useRef, useState } from 'react';
import { NavLink, useLocation, useNavigate } from 'react-router-dom';
import { useKeycloak } from '@react-keycloak/web';
import { useAuth } from '../../contexts/AuthContext';
import { useTheme } from '../../contexts/ThemeContext';
import { fetchCompanies, roleName } from '../../services/companyService';
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
const TABS = [
  { to: '/dashboard', label: 'Analizler', match: (p) => p === '/' || p === '/dashboard', countKey: 'analyses' },
  { to: '/data', label: 'Veri kaynakları', match: (p) => p.startsWith('/data'), countKey: 'connections' },
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

  // Sirket rozeti: taslakta "Enco Endustri A.S." sabit yaziyordu, biz gercek
  // sirket adini okuyoruz — bulunamazsa rozet hic gorunmez, uydurma isim
  // konmaz.
  //
  // Onceden bu blok "company_id claim'i yoksa hic isteme" diye basliyordu ve
  // rozet bos kaliyordu: keycloak.tokenParsed React state DEGIL, dolayisiyla
  // token sonradan yenilenip claim gelse bile Nav yeniden render olmuyor ve
  // effect bir daha calismiyordu. Artik claim'den bagimsiz olarak listeyi
  // cekiyoruz; eslesme claim varsa onunla, yoksa (kullanicinin tek firmasi
  // varsa) tek kayitla kuruluyor.
  const companyId = keycloak.tokenParsed?.company_id;
  const [companyName, setCompanyName] = useState('');
  useEffect(() => {
    let cancelled = false;
    fetchCompanies(keycloak.token)
      .then((list) => {
        if (cancelled) return;
        const company =
          (companyId && list.find((c) => c.id === companyId)) ||
          (list.length === 1 ? list[0] : null);
        if (company?.name) setCompanyName(company.name);
      })
      .catch((err) => {
        // Sessizce yutma: rozetin neden bos oldugu gorunur olsun.
        console.warn('Şirket adı okunamadı, rozet gizlenecek:', err?.message ?? err);
      });
    return () => {
      cancelled = true;
    };
    // location: rota degisiminde tekrar denenir — ilk yuklemede token henuz
    // hazir degilse rozet sonraki gezinmede kendini toparlar.
  }, [companyId, keycloak.token, location.pathname]);

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
          {TABS.map((t) => {
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
