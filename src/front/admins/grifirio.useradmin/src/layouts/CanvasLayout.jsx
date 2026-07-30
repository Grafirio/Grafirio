import React from 'react';
import { Outlet } from 'react-router-dom';

// Tam ekran layout — Header, container veya padding yok
// Canvas (Figma-like) sayfaları için kullanılır
const CanvasLayout = () => {
  return (
    <div style={{
      position: 'fixed',
      inset: 0,
      width: '100vw',
      height: '100vh',
      overflow: 'hidden',
      display: 'flex',
      flexDirection: 'column',
      zIndex: 9999,
    }}>
      <Outlet />
    </div>
  );
};

export default CanvasLayout;
