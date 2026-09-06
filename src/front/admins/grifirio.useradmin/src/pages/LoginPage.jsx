import React, { useEffect } from 'react';
import { Navigate } from 'react-router-dom';
import { useAuth } from '../contexts/AuthContext';

const LoginPage = () => {
  const { login, isAuthenticated, keycloak } = useAuth();

  // Kosulsuz login() cagirmak sonsuz donguydu: Keycloak'ta acik bir SSO oturumu
  // varken bu sayfaya dusen kullanici parola sorulmadan geri yollaniyor, sayfa
  // yeniden binip login()'i tekrar cagiriyordu. Her tur yeni bir kod ve yeni bir
  // token uretiyor, tarayici de iki adres arasinda takilip kaliyordu.
  useEffect(() => {
    if (isAuthenticated || keycloak.isDesktop) return;
    login();
  }, [isAuthenticated, login, keycloak]);

  if (isAuthenticated) {
    return <Navigate to="/" replace />;
  }

  return (
    <div className="page page-center">
      <div className="container container-tight py-4 text-center">
        {keycloak.isDesktop ? (
          <>
            <h1>Oturum açın</h1>
            <p className="text-muted">Giriş işlemi masaüstü uygulaması tarafından tarayıcınızda açılır.</p>
            <button type="button" className="btn btn-primary" onClick={login}>Giriş yap</button>
          </>
        ) : (
          <div className="text-muted">Yönlendiriliyor...</div>
        )}
      </div>
    </div>
  );
};

export default LoginPage;
