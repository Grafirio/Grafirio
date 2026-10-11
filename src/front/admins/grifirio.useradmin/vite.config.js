import { defineConfig, loadEnv } from 'vite';
import plugin from '@vitejs/plugin-react';
import process from 'node:process';

// https://vitejs.dev/config/
export default defineConfig(({ command, mode }) => {
    const environment = loadEnv(mode, process.cwd(), 'VITE_');
    if (command === 'build' && mode === 'production') {
        const address = process.env.VITE_API_URL || environment.VITE_API_URL;
        if (!address) throw new Error('Production build requires VITE_API_URL.');
        const endpoint = new URL(address);
        if (endpoint.protocol !== 'https:' || ['localhost', '127.0.0.1', '[::1]'].includes(endpoint.hostname)) {
            throw new Error('Production VITE_API_URL must use HTTPS and must not point to localhost.');
        }
    }
    return {
    plugins: [plugin()],
    base: '/',
    server: {
        port: 59264,
        host: '0.0.0.0',
        proxy: {
            '/data-analysis': {
                target: 'http://localhost:5221',
                changeOrigin: true,
                rewrite: (path) => path.replace(/^\/data-analysis/, ''),
            },
        },
    },
    preview: {
        port: 59264,
        host: '0.0.0.0',
        strictPort: true,
        // Uygulama Container Apps'te "vite preview" ile yayinlaniyor ve Vite
        // tanimadigi Host basligini 403 ile reddediyor. Her yeni alan adinda
        // listeyi guncellemek yerine kontrol kapatildi: onunde zaten yalnizca
        // kendi ingress'imiz var, dogrudan disari acik degil.
        allowedHosts: true,
    },
    build: {
        outDir: 'dist',
        assetsDir: 'assets',
        sourcemap: false,
    },
    };
});
