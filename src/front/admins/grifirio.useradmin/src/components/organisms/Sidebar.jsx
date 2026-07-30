import React, { useState } from 'react';
import { NavLink } from 'react-router-dom';
import {
  IconDashboard,
  IconBuilding,
  IconSettings,
  IconUsers,
  IconFileText,
  IconKey,
  IconUserCog,
  IconId,
  IconBrain,
  IconDatabase,
  IconChartBar
} from '@tabler/icons-react';

const menuItems = [
  { path: '/dashboard', name: 'Dashboard', icon: <IconDashboard size={24} /> },
  {
    name: 'Şirket Bilgileri',
    icon: <IconBuilding size={24} />,
    subItems: [
      { path: '/company-info', name: 'Şirket Bilgileri', icon: <IconFileText size={24} /> },
      { path: '/departments', name: 'Departmanlar', icon: <IconUsers size={24} /> },
    ],
  },
  {
    name: 'Veri Analizi',
    icon: <IconChartBar size={24} />,
    subItems: [
      { path: '/data-analysis', name: 'Veri Analizi', icon: <IconDatabase size={24} /> },
      { path: '/settings/sql-connection', name: 'SQL Bağlantıları', icon: <IconDatabase size={24} /> },
    ],
  },
  {
    name: 'Ayarlar',
    icon: <IconSettings size={24} />,
    subItems: [
      { path: '/settings/company', name: 'Şirket Ayarları', icon: <IconBuilding size={24} /> },
      { path: '/settings/data-input', name: 'Veri Girdisi', icon: <IconFileText size={24} /> },
      { path: '/settings/department', name: 'Departman Ayarları', icon: <IconUsers size={24} /> },
      { path: '/settings/authorization', name: 'Yetki Ayarları', icon: <IconKey size={24} /> },
      { path: '/settings/user', name: 'Kullanıcı Ayarları', icon: <IconUserCog size={24} /> },
      { path: '/settings/membership', name: 'Üyelik Bilgileri', icon: <IconId size={24} /> },
    ],
  },
];

const Sidebar = () => {
  const [openMenus, setOpenMenus] = useState({});

  const handleToggle = (name) => {
    setOpenMenus((prev) => ({ ...prev, [name]: !prev[name] }));
  };

  return (
    <aside className="navbar navbar-vertical navbar-expand-lg navbar-light">
      <div className="container-fluid">
        <h1 className="navbar-brand navbar-brand-autodark">
           <a href=".">
            <img src="https://preview.tabler.io/static/logo-white.svg" width="110" height="32" alt="Tabler" className="navbar-brand-image" />
          </a>
        </h1>
        <div className="collapse navbar-collapse" id="sidebar-menu">
          <ul className="navbar-nav pt-lg-3">
            {menuItems.map((item, idx) => {
              if (item.subItems) {
                return (
                  <li className="nav-item" key={item.name}>
                    <button
                      type="button"
                      className="nav-link d-flex align-items-center w-100"
                      style={{ background: 'none', border: 'none', padding: 0 }}
                      onClick={() => handleToggle(item.name)}
                    >
                      <span className="nav-link-icon d-md-none d-lg-inline-block">{item.icon}</span>
                      <span className="nav-link-title flex-grow-1 text-start">{item.name}</span>
                      <span style={{ marginLeft: 'auto', transition: 'transform 0.2s', transform: openMenus[item.name] ? 'rotate(90deg)' : 'rotate(0deg)' }}>&#9654;</span>
                    </button>
                    {openMenus[item.name] && (
                      <ul className="nav flex-column ms-4">
                        {item.subItems.map((sub) => (
                          <li className="nav-item" key={sub.path}>
                            <NavLink className="nav-link" to={sub.path}>
                              <span className="nav-link-icon d-md-none d-lg-inline-block">{sub.icon}</span>
                              <span className="nav-link-title">{sub.name}</span>
                            </NavLink>
                          </li>
                        ))}
                      </ul>
                    )}
                  </li>
                );
              } else {
                return (
                  <li className="nav-item" key={item.path}>
                    <NavLink className="nav-link" to={item.path}>
                      <span className="nav-link-icon d-md-none d-lg-inline-block">{item.icon}</span>
                      <span className="nav-link-title">{item.name}</span>
                    </NavLink>
                  </li>
                );
              }
            })}
          </ul>
        </div>
      </div>
    </aside>
  );
};

export default Sidebar;
