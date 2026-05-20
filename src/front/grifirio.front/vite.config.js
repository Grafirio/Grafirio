import { defineConfig } from 'vite';
import plugin from '@vitejs/plugin-react';

// https://vitejs.dev/config/
export default defineConfig({
    plugins: [plugin()],
    base: '/',
    server: {
        port: 59264,
        host: '0.0.0.0'
    },
    preview: {
        port: 59264,
        host: '0.0.0.0',
        strictPort: true,
        // Allow custom test domain behind Azure Container Apps ingress.
        // Frontend-only deploy trigger: keep this comment in sync with domain rollout.
        allowedHosts: [
            'test.grafirio.com'
        ]
    },
    build: {
        outDir: 'dist',
        assetsDir: 'assets',
        sourcemap: false
    }
})