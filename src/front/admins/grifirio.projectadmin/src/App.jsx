import { Navigate, Route, Routes } from 'react-router-dom';
import AdminLayout from './layouts/AdminLayout';
import CompaniesPage from './pages/CompaniesPage';
import CompanyDetailPage from './pages/CompanyDetailPage';
import ServiceDashboard from './components/ServiceDashboard';
import './App.css';

export default function App() {
  return (
    <Routes>
      <Route element={<AdminLayout />}>
        <Route index element={<Navigate to="/companies" replace />} />
        <Route path="companies" element={<CompaniesPage />} />
        <Route path="companies/:companyId" element={<CompanyDetailPage />} />
        {/* Yerel gelistirme araci; vizyondaki panelle ilgisi yok ama ise
            yaradigi icin ayri bir sekmede korunuyor. */}
        <Route path="services" element={<ServiceDashboard />} />
        <Route path="*" element={<Navigate to="/companies" replace />} />
      </Route>
    </Routes>
  );
}
