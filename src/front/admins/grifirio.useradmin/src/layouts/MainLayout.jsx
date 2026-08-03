import React from 'react';
import Nav from '../components/organisms/Nav';

const MainLayout = ({ children }) => {
  return (
    <div className="page">
      <Nav />
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
