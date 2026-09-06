import { useCallback, useState } from 'react';
import { ReactKeycloakProvider } from '@react-keycloak/web';

export default function DesktopKeycloakProvider({ children, ...props }) {
  const [, setTokens] = useState();
  const handleTokens = useCallback((tokens) => setTokens(tokens), []);

  // The upstream PureComponent does not rerender on same-authentication token rotations.
  // A fresh children element makes updated claims visible to every useKeycloak consumer.
  return (
    <ReactKeycloakProvider {...props} autoRefreshToken={false} onTokens={handleTokens}>
      <>{children}</>
    </ReactKeycloakProvider>
  );
}