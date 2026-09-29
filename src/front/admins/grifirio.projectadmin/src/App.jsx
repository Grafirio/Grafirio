import { Navigate, Route, Routes } from 'react-router-dom';
import AdminLayout from './layouts/AdminLayout';
import CompaniesPage from './pages/CompaniesPage';
import CompanyDetailPage from './pages/CompanyDetailPage';
import SemanticPage from './pages/SemanticPage';
import ServiceDashboard from './components/ServiceDashboard';
import './App.css';

export default function App() {
  return (
    <Routes>
      <Route element={<AdminLayout />}>
        <Route index element={<Navigate to="/semantic" replace />} />
        <Route path="companies" element={<CompaniesPage />} />
        <Route path="companies/:companyId" element={<CompanyDetailPage />} />
        {/* Semantik zeka olcumleri: panelin asil amaci bu gostergeler. */}
        <Route path="semantic" element={<SemanticPage />} />
        {/* Yerel gelistirme araci; vizyondaki panelle ilgisi yok ama ise
            yaradigi icin ayri bir sekmede korunuyor. */}
        <Route path="services" element={<ServiceDashboard />} />
        <Route path="*" element={<Navigate to="/semantic" replace />} />
      </Route>
    </Routes>
  );
}
