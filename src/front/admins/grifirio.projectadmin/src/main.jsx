import { StrictMode } from 'react';
import { createRoot } from 'react-dom/client';
import { BrowserRouter } from 'react-router-dom';
import { ReactKeycloakProvider } from '@react-keycloak/web';
import keycloak from './keycloak';
import App from './App.jsx';
import './styles/theme.css';
import './styles/projectadmin.css';
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
        // Derin linkler girisden sonra kaybolmasin.
        redirectUri: window.location.href,
      }}
    >
      <BrowserRouter>
        <App />
      </BrowserRouter>
    </ReactKeycloakProvider>
  </StrictMode>,
);
