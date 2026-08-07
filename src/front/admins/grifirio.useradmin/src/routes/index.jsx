import { Navigate } from 'react-router-dom';
import ProtectedRoute from './ProtectedRoute';
import AuthLayout from '../layouts/AuthLayout';
import CanvasLayout from '../layouts/CanvasLayout';

// Sayfalar
import LoginPage from '../pages/LoginPage';
import OnboardingPage from '../pages/OnboardingPage';
import DashboardPage from '../pages/DashboardPage';
import CanvasPage from '../pages/CanvasPage';
import CompanyAdminPage from '../pages/CompanyAdminPage';

// Ayar Sayfaları — menudeki her giris kendi sayfasina gidiyor.
import CompanySettingsPage from '../pages/settings/CompanySettingsPage';
import DataEntryPage from '../pages/settings/DataEntryPage';
import DepartmentsPage from '../pages/settings/DepartmentsPage';
import RolesPage from '../pages/settings/RolesPage';
import UsersPage from '../pages/settings/UsersPage';
import SqlConnectionSettings from '../pages/SqlConnectionSettings';
import PlanPage from '../pages/PlanPage';

const routes = [
  {
    path: '/',
    element: <ProtectedRoute />,
    children: [
      { path: '/', element: <DashboardPage /> },
      { path: 'dashboard', element: <DashboardPage /> },
      { path: 'company-info', element: <CompanyAdminPage /> },
      // Kanvasla ayni isi yapan eski ekranlar kaldirildi: /ai-query kendi
      // arka ucuna, /ai-dashboard Django'ya ve dogrudan localhost portlarina,
      // /data-analysis ise kanvastan onceki analiz akisina bagliydi. Ucu de
      // ayni soruyu farkli cevaplayan ekranlardi. Eski baglantilar kirilmasin
      // diye kanvasa yonlendiriliyorlar.
      { path: 'ai-query', element: <Navigate to="/canvas" replace /> },
      { path: 'ai-dashboard', element: <Navigate to="/canvas" replace /> },
      { path: 'data-analysis', element: <Navigate to="/canvas" replace /> },
      {
        path: 'settings',
        children: [
          { path: 'company', element: <CompanySettingsPage /> },
          { path: 'data-input', element: <DataEntryPage /> },
          { path: 'sql-connection', element: <SqlConnectionSettings /> },
          { path: 'department', element: <DepartmentsPage /> },
          { path: 'authorization', element: <RolesPage /> },
          { path: 'user', element: <UsersPage /> },
          { path: 'membership', element: <PlanPage /> },
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
