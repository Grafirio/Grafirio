import ProtectedRoute from './ProtectedRoute';
import AuthLayout from '../layouts/AuthLayout';
import CanvasLayout from '../layouts/CanvasLayout';

// Sayfalar
import LoginPage from '../pages/LoginPage';
import OnboardingPage from '../pages/OnboardingPage';
import DashboardPage from '../pages/DashboardPage';
import CanvasPage from '../pages/CanvasPage';
import CompanyAdminPage from '../pages/CompanyAdminPage';
import AIPage from '../pages/AIPage';
import DataAnalysisPage from '../pages/DataAnalysis/DataAnalysisPage';
import AgentQueryPage from '../pages/AgentQueryPage';

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
      { path: 'ai-dashboard', element: <AIPage /> },
      { path: 'data-analysis', element: <DataAnalysisPage /> },
      { path: 'ai-query', element: <AgentQueryPage /> },
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
  // Public routes for development/testing (without Keycloak protection)
  {
    path: '/ai-dashboard-public',
    element: <AIPage />,
  },
  {
    path: '/data-analysis-public',
    element: <DataAnalysisPage />,
  },
  // Alternative: Test page at root level for quick access
  {
    path: '/test-data-analysis',
    element: <DataAnalysisPage />,
  },
  // Diğer rotalar (404 vb.) buraya eklenebilir
];

// ── Canvas route: full-screen, kendi layout'u var ──
// Auth gereksiz (geliştirme kolaylığı için) — üretimde ProtectedRoute'a taşı
const canvasRoutes = [
  {
    path: '/canvas/:analysisId',
    element: <CanvasLayout />,
    children: [
      { path: '', element: <CanvasPage /> },
    ],
  },
];

export { canvasRoutes };
export default routes;
