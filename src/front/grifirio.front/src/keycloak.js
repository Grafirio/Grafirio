import Keycloak from 'keycloak-js';

// Keycloak konfigürasyonu
const keycloakConfig = {
  url: 'http://localhost:8080',
  realm: 'master',
  clientId: 'Grifirio',
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
