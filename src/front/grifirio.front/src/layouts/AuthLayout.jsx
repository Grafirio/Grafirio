import React from 'react';
import { Outlet } from 'react-router-dom';

// Bu layout, login/register gibi kimlik doğrulama sayfaları için kullanılır.
// Sidebar veya Header içermez.
const AuthLayout = () => {
  return (
    <div className="page page-center">
      <div className="container container-tight py-4">
        <div className="text-center mb-4">
          <a href="." className="navbar-brand navbar-brand-autodark">
            <img src="https://preview.tabler.io/static/logo.svg" height="36" alt="Grifirio" />
          </a>
        </div>
        <Outlet />
      </div>
    </div>
  );
};

export default AuthLayout;
