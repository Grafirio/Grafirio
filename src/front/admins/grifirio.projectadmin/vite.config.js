import { defineConfig } from 'vite';
import plugin from '@vitejs/plugin-react';

// https://vitejs.dev/config/
export default defineConfig({
    plugins: [plugin()],
    server: {
        port: 59666,
    },
    preview: {
        port: 59265,
        host: '0.0.0.0',
        strictPort: true,
        // Uygulama Container Apps'te "vite preview" ile yayinlaniyor ve Vite
        // tanimadigi Host basligini 403 ile reddediyor. Her yeni alan adinda
        // listeyi guncellemek yerine kontrol kapatildi: onunde zaten yalnizca
        // kendi ingress'imiz var, dogrudan disari acik degil.
        allowedHosts: true,
    },
});
