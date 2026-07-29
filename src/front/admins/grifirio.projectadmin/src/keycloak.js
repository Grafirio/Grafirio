import Keycloak from 'keycloak-js';

const keycloakConfig = {
  url: import.meta.env.VITE_KEYCLOAK_URL || 'http://localhost:8080',
  realm: import.meta.env.VITE_KEYCLOAK_REALM || 'grafirio',
  clientId: import.meta.env.VITE_KEYCLOAK_CLIENT_ID || 'grafirio-client',
};

// Singleton: birden fazla Keycloak örneği init edilirse ikincisi hata veriyor.
let instance = null;

const getKeycloak = () => {
  if (!instance) {
    instance = new Keycloak(keycloakConfig);
  }
  return instance;
};

export default getKeycloak();
