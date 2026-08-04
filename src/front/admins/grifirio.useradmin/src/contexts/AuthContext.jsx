import React, { createContext, useCallback, useContext } from 'react';
import { useKeycloak } from '@react-keycloak/web';
import GMark from '../components/organisms/GMark';
import '../styles/Boot.css';

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

  // Sabitlenmeleri sart: bunlari bagimliliga alan bir effect, her render'da yeni
  // bir fonksiyon gorup tekrar calisirdi.
  const login = useCallback(() => {
    try {
      keycloak.login();
    } catch (error) {
      console.error('Login hatası:', error);
    }
  }, [keycloak]);

  const logout = useCallback(() => {
    try {
      keycloak.logout();
    } catch (error) {
      console.error('Logout hatası:', error);
    }
  }, [keycloak]);

  const isAuthenticated = keycloak.authenticated;

  // Keycloak henüz initialize olmadıysa loading göster.
  // Bu iki ekran panelin ilk gorunen yuzu; onceden Tabler'in ornek logosunu
  // (preview.tabler.io) gosteriyorlardi — yabanci bir markanin logosu ve
  // ayrica dis bir sunucuya bagimlilik.
  if (!initialized) {
    return (
      <div className="boot">
        <GMark size={44} />
        <p className="boot-text">Kimlik doğrulama sistemi yükleniyor…</p>
        <span className="boot-bar" />
      </div>
    );
  }

  // Keycloak initialization hatası varsa göster
  if (keycloak.initError) {
    return (
      <div className="boot">
        <GMark size={44} />
        <h1 className="boot-title">Kimlik doğrulama hatası</h1>
        <p className="boot-text">
          Kimlik sunucusuna bağlanılamadı. Bağlantınızı kontrol edip sayfayı yenileyin.
        </p>
        <button type="button" className="boot-btn" onClick={() => window.location.reload()}>
          Sayfayı yenile
        </button>
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
