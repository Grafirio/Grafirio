import ProtectedRoute from './ProtectedRoute';
import AuthLayout from '../layouts/AuthLayout';

// Sayfalar
import LoginPage from '../pages/LoginPage';
import DashboardPage from '../pages/DashboardPage';
import CompanyInfoPage from '../pages/CompanyInfoPage';
import DepartmentsPage from '../pages/DepartmentsPage';

// Ayar Sayfaları (şimdilik hepsi aynı placeholder'ı kullanabilir)
import SettingsPage from '../pages/SettingsPage';

const routes = [
  {
    path: '/',
    element: <ProtectedRoute />,
    children: [
      { path: '/', element: <DashboardPage /> },
      { path: 'dashboard', element: <DashboardPage /> },
      { path: 'company-info', element: <CompanyInfoPage /> },
      { path: 'departments', element: <DepartmentsPage /> },
      {
        path: 'settings',
        children: [
          { path: 'company', element: <SettingsPage title="Şirket Ayarları" /> },
          { path: 'data-input', element: <SettingsPage title="Veri Girdisi" /> },
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
  // Diğer rotalar (404 vb.) buraya eklenebilir
];

export default routes;
