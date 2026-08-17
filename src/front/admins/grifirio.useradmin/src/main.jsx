import React from 'react';
import ReactDOM from 'react-dom/client';
import { BrowserRouter } from 'react-router-dom';
import { ReactKeycloakProvider } from '@react-keycloak/web';
import keycloak from './keycloak';
import App from './App';
import { AuthProvider } from './contexts/AuthContext';
import { CompanyProvider } from './contexts/CompanyProvider';
import { ThemeProvider } from './contexts/ThemeContext';
import './styles/theme.css';
// Bootstrap JS for interactive components
import 'bootstrap/dist/js/bootstrap.bundle.min.js';

// Keycloak event callback'leri
const eventLogger = (event, error) => {
  console.log('Keycloak event:', event);
  if (error) {
    console.error('Keycloak error:', error);
  }
  
  // Specific event handling
  switch (event) {
    case 'onInitError':
      console.error('Keycloak initialization failed:', error);
      break;
    case 'onAuthError':
      console.error('Keycloak authentication failed:', error);
      break;
    case 'onAuthSuccess':
      console.log('Keycloak authentication successful');
      break;
    case 'onTokenExpired':
      console.log('Keycloak token expired');
      break;
  }
};

const tokenLogger = (tokens) => {
  console.log('Keycloak tokens updated:', tokens);
};

// StrictMode'u geçici olarak kaldırıyoruz çünkü Keycloak ile uyumsuzluk yaşıyor
ReactDOM.createRoot(document.getElementById('root')).render(
  <ReactKeycloakProvider
    authClient={keycloak}
    onEvent={eventLogger}
    onTokens={tokenLogger}
    initOptions={{
      // Masaustu uygulamasinin devrettigi oturum.
      //
      // Giris orada DIS TARAYICIDA yapiliyor; bu pencerenin Keycloak
      // cerezi yok ve 'login-required' kullaniciyi ikinci kez giris
      // yapmaya zorlardi. keycloak-js hazir token'larla baslatilabiliyor:
      // uygulama token'lari sayfa yuklenmeden once enjekte ediyor, buradan
      // da olduklari gibi veriliyor. Yenileme yine keycloak-js'in isi —
      // refresh token'la token ucuna gidiyor, cereze ihtiyaci yok.
      ...(window.__GRAFIRIO_DESKTOP__?.tokens ?? {}),
      onLoad: window.__GRAFIRIO_DESKTOP__?.tokens ? 'check-sso' : 'login-required',
      checkLoginIframe: false,
      enableLogging: true,
      flow: 'standard',
      responseMode: 'fragment',
      // Deliberately not pinned to the origin: a hardcoded '/' sent every
      // full page load back to the dashboard, so deep links and refreshes
      // could never reach the page they asked for.
      redirectUri: window.location.href,
      // CORS için önemli
      adapter: 'default',
    }}
  >
    <BrowserRouter>
      <ThemeProvider>
        <AuthProvider>
          <CompanyProvider>
            <App />
          </CompanyProvider>
        </AuthProvider>
      </ThemeProvider>
    </BrowserRouter>
  </ReactKeycloakProvider>
);
