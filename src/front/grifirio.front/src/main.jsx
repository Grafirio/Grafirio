import React from 'react';
import ReactDOM from 'react-dom/client';
import { BrowserRouter } from 'react-router-dom';
import { ReactKeycloakProvider } from '@react-keycloak/web';
import keycloak from './keycloak';
import App from './App';
import { AuthProvider } from './contexts/AuthContext';
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
      onLoad: 'login-required',
      checkLoginIframe: false,
      enableLogging: true,
      flow: 'standard',
      responseMode: 'fragment',
      redirectUri: window.location.origin + '/',
      // CORS için önemli
      adapter: 'default',
    }}
  >
    <BrowserRouter>
      <AuthProvider>
        <App />
      </AuthProvider>
    </BrowserRouter>
  </ReactKeycloakProvider>
);
