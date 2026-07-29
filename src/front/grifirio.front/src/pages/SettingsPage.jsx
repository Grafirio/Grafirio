import React from 'react';

const SettingsPage = ({ title }) => {
  return (
    <div className="gf-page">
      <div className="gf-page-header">
        <h1 className="gf-page-title">{title}</h1>
      </div>
      <p className="gf-muted">Burası bir ayar sayfasıdır.</p>
    </div>
  );
};

export default SettingsPage;
