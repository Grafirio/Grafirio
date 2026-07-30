import { defineConfig } from 'vite';
import plugin from '@vitejs/plugin-react';

// https://vitejs.dev/config/
export default defineConfig({
    plugins: [plugin()],
    base: '/',
    server: {
        // Vitrin hicbir servise proxy'lemiyor; urun uygulamasi 59264'u kullandigi
        // icin burasi Vite'in varsayilaninda birakildi ve ikisi ayni anda
        // calisabiliyor.
        port: 5173,
        host: '0.0.0.0',
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
});
