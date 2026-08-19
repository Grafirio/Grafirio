import { Navigate } from 'react-router-dom';
import ProtectedRoute from './ProtectedRoute';
import AuthLayout from '../layouts/AuthLayout';
import CanvasLayout from '../layouts/CanvasLayout';

// Sayfalar
import LoginPage from '../pages/LoginPage';
import OnboardingPage from '../pages/OnboardingPage';
import DashboardPage from '../pages/DashboardPage';
import CanvasPage from '../pages/CanvasPage';
import DataSourcesPage from '../pages/DataSourcesPage';
import SettingsHubPage from '../pages/SettingsHubPage';
import UsersRolesPage from '../pages/UsersRolesPage';

// Ayar Sayfaları — menudeki her giris kendi sayfasina gidiyor.
import CompanySettingsPage from '../pages/settings/CompanySettingsPage';
import PermissionsPage from '../pages/settings/PermissionsPage';
import ThemeSettingsPage from '../pages/settings/ThemeSettingsPage';
import NotificationsPage from '../pages/settings/NotificationsPage';
import PlanPage from '../pages/PlanPage';

const routes = [
  {
    path: '/',
    element: <ProtectedRoute />,
    children: [
      { path: '/', element: <DashboardPage /> },
      { path: 'dashboard', element: <DashboardPage /> },
      { path: 'data', element: <DataSourcesPage /> },
      // Kanvasla ayni isi yapan eski ekranlar kaldirildi: /ai-query kendi
      // arka ucuna, /ai-dashboard Django'ya ve dogrudan localhost portlarina,
      // /data-analysis ise kanvastan onceki analiz akisina bagliydi. Ucu de
      // ayni soruyu farkli cevaplayan ekranlardi. Eski baglantilar kirilmasin
      // diye kanvasa yonlendiriliyorlar.
      { path: 'ai-query', element: <Navigate to="/canvas" replace /> },
      { path: 'ai-dashboard', element: <Navigate to="/canvas" replace /> },
      { path: 'data-analysis', element: <Navigate to="/canvas" replace /> },
      // Menu artik dropdown degil sekme + hub: "Sirket Bilgileri" ve
      // "Baglanti Ayarlari" acilir menuleri kalkti, /company-info ve
      // /settings/sql-connection gibi eski yollar kirilmasin diye buradan
      // yeni karsiliklarina yonlendiriliyor.
      { path: 'company-info', element: <Navigate to="/settings/company" replace /> },
      {
        path: 'settings',
        children: [
          { index: true, element: <SettingsHubPage /> },
          { path: 'company', element: <CompanySettingsPage /> },
          { path: 'users', element: <UsersRolesPage /> },
          { path: 'permissions', element: <PermissionsPage /> },
          // Departmanlar artik Izinler sayfasinin bir sekmesi; eski adres ve
          // yer imleri kirilmasin diye oraya yonlendiriliyor.
          {
            path: 'department',
            element: <Navigate to="/settings/permissions?tab=departments" replace />,
          },
          { path: 'theme', element: <ThemeSettingsPage /> },
          { path: 'notifications', element: <NotificationsPage /> },
          { path: 'membership', element: <PlanPage /> },
          { path: 'data-input', element: <Navigate to="/data?tab=upload" replace /> },
          { path: 'sql-connection', element: <Navigate to="/data?tab=connections" replace /> },
          { path: 'authorization', element: <Navigate to="/settings/permissions" replace /> },
          { path: 'user', element: <Navigate to="/settings/users" replace /> },
        ],
      },
    ],
  },
  {
    path: '/login',
    element: <AuthLayout />,
    children: [
      { path: '', element: <LoginPage /> },
    ],
  },
  // Karsilama sihirbazi bilerek ProtectedRoute'un disinda: oraya giden
  // kullanicinin henuz firmasi ve abonelii yok, yani erisim kontrolu onu
  // tam da ihtiyaci olan sayfadan geri cevirirdi. Giris zorunlulugunu
  // Keycloak'in "login-required" ayari zaten sagliyor.
  {
    path: '/onboarding',
    element: <OnboardingPage />,
  },
  // Kimlik dogrulamasiz "-public" ve "test-" rotalari kaldirildi: gelistirme
  // kolayligi icin acilmislardi ama uretimde de duruyorlardi.
  // Diğer rotalar (404 vb.) buraya eklenebilir
];

// ── Canvas route: full-screen, kendi layout'u var ──
//
// Artik ProtectedRoute'un altinda. Onceden "gelistirme kolayligi icin" acikti
// ve dosyada "uretimde ProtectedRoute'a tasi" notu duruyordu; arkadaki uclar
// zaten CompanyAccess istedigi icin veri sizmiyordu ama giris yapmamis
// kullanici bos bir kanvasla karsilasiyordu.
//
// Tek giris yolu var: /canvas?connectionId=... — veritabani adi ve secili
// tablolar sunucudan okunuyor. Eski /canvas/:analysisId yolu kaldirildi;
// kaydi tarayicinin localStorage'indan okudugu icin baska bir makineden
// girildiginde kanvas bos aciliyordu.
const canvasRoutes = [
  {
    path: '/canvas',
    element: <ProtectedRoute />,
    children: [
      {
        path: '',
        element: <CanvasLayout />,
        children: [
          { path: '', element: <CanvasPage /> },
        ],
      },
    ],
  },
];

export { canvasRoutes };
export default routes;
