import { StrictMode } from 'react';
import { createRoot } from 'react-dom/client';
import { BrowserRouter } from 'react-router-dom';
import { ReactKeycloakProvider } from '@react-keycloak/web';
import keycloak from './keycloak';
import App from './App.jsx';
import './styles/theme.css';
import './styles/projectadmin.css';
import './styles/semantic.css';
import './index.css';

createRoot(document.getElementById('root')).render(
  <StrictMode>
    <ReactKeycloakProvider
      authClient={keycloak}
      initOptions={{
        // check-sso: giris zorunlu tutulmuyor ki yetkisiz kullaniciya
        // Keycloak dongusu yerine acik bir "yetkiniz yok" ekrani gosterebilelim.
        onLoad: 'check-sso',
        checkLoginIframe: false,
        pkceMethod: 'S256',
        // BrowserRouter deep links live in path/search. The fragment can contain
        // a previous OIDC error/code; carrying it into redirect_uri nests the
        // old callback inside the next login response.
        redirectUri: `${window.location.origin}${window.location.pathname}${window.location.search}`,
      }}
    >
      <BrowserRouter>
        <App />
      </BrowserRouter>
    </ReactKeycloakProvider>
  </StrictMode>,
);
