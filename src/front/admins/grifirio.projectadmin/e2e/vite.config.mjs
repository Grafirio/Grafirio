import { fileURLToPath } from 'node:url';
import { defineConfig } from 'vite';
import react from '@vitejs/plugin-react';

// Separate test entry. Production index/main/config never import this fixture.
export default defineConfig({
  root: fileURLToPath(new URL('.', import.meta.url)),
  envDir: false,
  plugins: [react()],
  resolve: {
    alias: {
      '@react-keycloak/web': fileURLToPath(new URL('./keycloak-fixture.jsx', import.meta.url)),
    },
  },
  define: {
    'import.meta.env.VITE_GATEWAY_URL': JSON.stringify(''),
    'import.meta.env.VITE_KEYCLOAK_URL': JSON.stringify('http://127.0.0.1:1'),
  },
  server: {
    host: '127.0.0.1',
    fs: { allow: [fileURLToPath(new URL('..', import.meta.url))] },
  },
});
