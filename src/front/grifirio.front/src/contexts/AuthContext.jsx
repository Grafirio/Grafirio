import React, { createContext, useContext } from 'react';
import { useKeycloak } from '@react-keycloak/web';

const AuthContext = createContext(null);

export const AuthProvider = ({ children }) => {
  const { keycloak, initialized } = useKeycloak();

  // Keycloak'tan kullanıcı bilgilerini al
  const user = keycloak.authenticated ? {
    id: keycloak.tokenParsed?.sub,
    name: keycloak.tokenParsed?.name || keycloak.tokenParsed?.preferred_username,
    email: keycloak.tokenParsed?.email,
    roles: keycloak.tokenParsed?.realm_access?.roles || [],
  } : null;

  const login = () => {
    try {
      keycloak.login();
    } catch (error) {
      console.error('Login hatası:', error);
    }
  };

  const logout = () => {
    try {
      keycloak.logout();
    } catch (error) {
      console.error('Logout hatası:', error);
    }
  };

  const isAuthenticated = keycloak.authenticated;

  // Keycloak henüz initialize olmadıysa loading göster
  if (!initialized) {
    return (
      <div className="page page-center">
        <div className="container container-tight py-4">
          <div className="text-center">
            <div className="mb-3">
              <a href="." className="navbar-brand navbar-brand-autodark">
                <img src="https://preview.tabler.io/static/logo.svg" height="36" alt="Grifirio" />
              </a>
            </div>
            <div className="text-muted mb-3">Kimlik doğrulama sistemi yükleniyor...</div>
            <div className="progress progress-sm">
              <div className="progress-bar progress-bar-indeterminate"></div>
            </div>
          </div>
        </div>
      </div>
    );
  }

  // Keycloak initialization hatası varsa göster
  if (keycloak.initError) {
    return (
      <div className="page page-center">
        <div className="container container-tight py-4">
          <div className="text-center">
            <div className="mb-3">
              <a href="." className="navbar-brand navbar-brand-autodark">
                <img src="https://preview.tabler.io/static/logo.svg" height="36" alt="Grifirio" />
              </a>
            </div>
            <div className="alert alert-danger">
              <h4 className="alert-title">Kimlik Doğrulama Hatası</h4>
              <div className="text-muted">
                Keycloak sunucusuna bağlanırken bir hata oluştu. Lütfen daha sonra tekrar deneyin.
              </div>
              <div className="btn-list mt-3">
                <button className="btn btn-primary" onClick={() => window.location.reload()}>
                  Sayfayı Yenile
                </button>
              </div>
            </div>
          </div>
        </div>
      </div>
    );
  }

  return (
    <AuthContext.Provider value={{ user, login, logout, isAuthenticated, keycloak }}>
      {children}
    </AuthContext.Provider>
  );
};

export const useAuth = () => {
  const context = useContext(AuthContext);
  if (!context) {
    throw new Error('useAuth must be used within an AuthProvider');
  }
  return context;
};
