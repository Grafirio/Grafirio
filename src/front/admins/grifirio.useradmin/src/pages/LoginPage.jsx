import React, { useEffect } from 'react';
import { Navigate } from 'react-router-dom';
import { useAuth } from '../contexts/AuthContext';

const LoginPage = () => {
  const { login, isAuthenticated } = useAuth();

  // Kosulsuz login() cagirmak sonsuz donguydu: Keycloak'ta acik bir SSO oturumu
  // varken bu sayfaya dusen kullanici parola sorulmadan geri yollaniyor, sayfa
  // yeniden binip login()'i tekrar cagiriyordu. Her tur yeni bir kod ve yeni bir
  // token uretiyor, tarayici de iki adres arasinda takilip kaliyordu.
  useEffect(() => {
    if (isAuthenticated) return;
    login();
  }, [isAuthenticated, login]);

  if (isAuthenticated) {
    return <Navigate to="/" replace />;
  }

  return (
    <div className="page page-center">
      <div className="container container-tight py-4 text-center">
        <div className="text-muted">Yönlendiriliyor...</div>
      </div>
    </div>
  );
};

export default LoginPage;
