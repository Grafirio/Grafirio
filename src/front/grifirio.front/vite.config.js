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
        // Allow wildcard hosts to completely bypass local host checking on Azure.
        allowedHosts: ['*']
    },
    build: {
        outDir: 'dist',
        assetsDir: 'assets',
        sourcemap: false
    }
})