import React from 'react';
import { useAuth } from '../contexts/AuthContext';

const LoginPage = () => {
  const { login } = useAuth();

  const handleLogin = () => {
    login(); // Keycloak login sayfasına yönlendirme
  };

  return (
    <div className="card card-md">
      <div className="card-body">
        <h2 className="h2 text-center mb-4">Hesabınıza giriş yapın</h2>
        <div className="text-center">
          <p className="text-muted mb-4">
            Güvenli giriş için Keycloak kimlik doğrulama sistemini kullanıyoruz.
          </p>
          <button type="button" className="btn btn-primary w-100" onClick={handleLogin}>
            <svg xmlns="http://www.w3.org/2000/svg" className="icon icon-tabler icon-tabler-login me-2" width="24" height="24" viewBox="0 0 24 24" strokeWidth="2" stroke="currentColor" fill="none" strokeLinecap="round" strokeLinejoin="round">
              <path stroke="none" d="M0 0h24v24H0z" fill="none"/>
              <path d="M14 8v-2a2 2 0 0 0 -2 -2h-7a2 2 0 0 0 -2 2v12a2 2 0 0 0 2 2h7a2 2 0 0 0 2 -2v-2"/>
              <path d="M3 12h13l-3 -3"/>
              <path d="M13 15l3 -3"/>
            </svg>
            Keycloak ile Giriş Yap
          </button>
          <div className="mt-3">
            <small className="text-muted">
              Giriş yaptıktan sonra otomatik olarak ana sayfaya yönlendirileceksiniz.
            </small>
          </div>
        </div>
      </div>
    </div>
  );
};

export default LoginPage;
