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
        // Allow all hosts behind Azure Ingress to bypass local host filtering.
        allowedHosts: true
    },
    build: {
        outDir: 'dist',
        assetsDir: 'assets',
        sourcemap: false
    }
})