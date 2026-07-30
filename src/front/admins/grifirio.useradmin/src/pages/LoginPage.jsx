import React, { useEffect } from 'react';
import { useAuth } from '../contexts/AuthContext';

const LoginPage = () => {
  const { login } = useAuth();

  useEffect(() => {
    login();
  }, []);

  return (
    <div className="page page-center">
      <div className="container container-tight py-4 text-center">
        <div className="text-muted">Yönlendiriliyor...</div>
      </div>
    </div>
  );
};

export default LoginPage;
