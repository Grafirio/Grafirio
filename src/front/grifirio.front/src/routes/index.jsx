import ProtectedRoute from './ProtectedRoute';
import AuthLayout from '../layouts/AuthLayout';

// Sayfalar
import LoginPage from '../pages/LoginPage';
import DashboardPage from '../pages/DashboardPage';
import CompanyInfoPage from '../pages/CompanyInfoPage';
import DepartmentsPage from '../pages/DepartmentsPage';
import AIPage from '../pages/AIPage';
import DataAnalysisPage from '../pages/DataAnalysis/DataAnalysisPage';
import AgentQueryPage from '../pages/AgentQueryPage';

// Ayar Sayfaları
import SettingsPage from '../pages/SettingsPage';
import SqlConnectionSettings from '../pages/SqlConnectionSettings';

const routes = [
  {
    path: '/',
    element: <ProtectedRoute />,
    children: [
      { path: '/', element: <DashboardPage /> },
      { path: 'dashboard', element: <DashboardPage /> },
      { path: 'company-info', element: <CompanyInfoPage /> },
      { path: 'departments', element: <DepartmentsPage /> },
      { path: 'ai-dashboard', element: <AIPage /> },
      { path: 'data-analysis', element: <DataAnalysisPage /> },
      { path: 'ai-query', element: <AgentQueryPage /> },
      {
        path: 'settings',
        children: [
          { path: 'company', element: <SettingsPage title="Şirket Ayarları" /> },
          { path: 'data-input', element: <SettingsPage title="Veri Girdisi" /> },
          { path: 'sql-connection', element: <SqlConnectionSettings /> },
          { path: 'department', element: <SettingsPage title="Departman Ayarları" /> },
          { path: 'authorization', element: <SettingsPage title="Yetki Ayarları" /> },
          { path: 'user', element: <SettingsPage title="Kullanıcı Ayarları" /> },
          { path: 'membership', element: <SettingsPage title="Üyelik Bilgileri" /> },
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

export default routes;
