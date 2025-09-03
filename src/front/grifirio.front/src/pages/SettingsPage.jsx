import React from 'react';

const SettingsPage = ({ title }) => {
  return (
    <div>
      <div className="page-header">
        <h1 className="page-title">{title}</h1>
      </div>
      <p>Burası bir ayar sayfasıdır.</p>
    </div>
  );
};

export default SettingsPage;
