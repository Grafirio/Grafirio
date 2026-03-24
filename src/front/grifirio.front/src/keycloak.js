import Keycloak from 'keycloak-js';

// Keycloak konfigürasyonu - Environment variable'lardan oku
const keycloakConfig = {
  url: import.meta.env.VITE_KEYCLOAK_URL || 'http://localhost:8080',
  realm: import.meta.env.VITE_KEYCLOAK_REALM || 'grafirio',
  clientId: import.meta.env.VITE_KEYCLOAK_CLIENT_ID || 'grafirio-client',
};

// Singleton pattern - Keycloak instance'ını sadece bir kez oluştur
let keycloakInstance = null;

const getKeycloakInstance = () => {
  if (!keycloakInstance) {
    keycloakInstance = new Keycloak(keycloakConfig);
    
    // Debug için konfigürasyon bilgilerini logla
    console.log('Keycloak Configuration:', {
      url: keycloakConfig.url,
      realm: keycloakConfig.realm,
      clientId: keycloakConfig.clientId,
      currentOrigin: window.location.origin,
      currentPort: window.location.port
    });
  }
  return keycloakInstance;
};

export default getKeycloakInstance();
