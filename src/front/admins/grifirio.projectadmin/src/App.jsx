import { Navigate, Route, Routes } from 'react-router-dom';
import AdminLayout from './layouts/AdminLayout';
import CompaniesPage from './pages/CompaniesPage';
import CompanyDetailPage from './pages/CompanyDetailPage';
import SemanticPage from './pages/SemanticPage';
import ObservabilityPage from './pages/ObservabilityPage';
import ServicesPage from './pages/ServicesPage';
import './App.css';

export default function App() {
  return (
    <Routes>
      <Route element={<AdminLayout />}>
        <Route index element={<Navigate to="/observability" replace />} />
        <Route path="observability" element={<ObservabilityPage />} />
        <Route path="companies" element={<CompaniesPage />} />
        <Route path="companies/:companyId" element={<CompanyDetailPage />} />
        <Route path="semantic" element={<SemanticPage />} />
        <Route path="services" element={<ServicesPage />} />
        <Route path="*" element={<Navigate to="/observability" replace />} />
      </Route>
    </Routes>
  );
}
