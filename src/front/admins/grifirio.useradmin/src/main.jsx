import React from 'react';
import ReactDOM from 'react-dom/client';
import { BrowserRouter } from 'react-router-dom';
import { ReactKeycloakProvider } from '@react-keycloak/web';
import keycloak from './auth/desktop/authClient.js';
import DesktopKeycloakProvider from './auth/desktop/DesktopKeycloakProvider.jsx';
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

const AuthKeycloakProvider = keycloak.isDesktop ? DesktopKeycloakProvider : ReactKeycloakProvider;

// StrictMode'u geçici olarak kaldırıyoruz çünkü Keycloak ile uyumsuzluk yaşıyor
ReactDOM.createRoot(document.getElementById('root')).render(
  <AuthKeycloakProvider
    authClient={keycloak}
    onEvent={eventLogger}
    initOptions={keycloak.isDesktop ? {} : {
      onLoad: 'login-required',
      checkLoginIframe: false,
      enableLogging: false,
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
  </AuthKeycloakProvider>
);
