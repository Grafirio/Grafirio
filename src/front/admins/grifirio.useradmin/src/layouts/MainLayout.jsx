import React from 'react';
import Header from '../components/organisms/Header';

const MainLayout = ({ children }) => {
  return (
    <div className="page">
      <Header />
      <div className="page-wrapper">
        <div className="page-body">
          <div className="container-xl">
            {children}
          </div>
        </div>
      </div>
    </div>
  );
};

export default MainLayout;
