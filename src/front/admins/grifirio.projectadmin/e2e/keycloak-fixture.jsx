// UI role fixture only. Does not test OIDC, token validation or API authorization.
export function useKeycloak() {
  const mode = window.__grafirioTestRole || 'anonymous';
  return {
    initialized: true,
    keycloak: {
      authenticated: mode !== 'anonymous',
      tokenParsed: { business_roles: mode === 'admin' ? ['PLATFORM_ADMIN'] : ['COMPANY_ADMIN'], preferred_username: 'synthetic-user' },
      login() {},
      logout() {},
    },
  };
}
