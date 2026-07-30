import React, { useState } from 'react';
import { testConnection } from '../../services/dataAnalysisService';
import './ConnectionForm.css';

const ConnectionForm = ({ onConnectionSuccess }) => {
  const [savedConnections, setSavedConnections] = useState([]);
  const [selectedConnectionId, setSelectedConnectionId] = useState('');
  
  const [formData, setFormData] = useState({
    host: '',
    port: 1433,
    database: '',
    username: '',
    password: '',
    trustServerCertificate: true
  });

  const [isLoading, setIsLoading] = useState(false);
  const [status, setStatus] = useState({ type: '', message: '' });

  // localStorage'dan kayıtlı bağlantıları yükle
  React.useEffect(() => {
    const connections = JSON.parse(localStorage.getItem('sqlConnections') || '[]');
    setSavedConnections(connections);
  }, []);

  const handleChange = (e) => {
    const { name, value, type, checked } = e.target;
    setFormData(prev => ({
      ...prev,
      [name]: type === 'checkbox' ? checked : value
    }));
  };

  const handleSavedConnectionSelect = (e) => {
    const connectionId = e.target.value;
    setSelectedConnectionId(connectionId);
    
    if (connectionId) {
      const connection = savedConnections.find(c => c.id === connectionId);
      if (connection) {
        setFormData({
          host: connection.host,
          port: connection.port,
          database: connection.database,
          username: connection.username,
          password: connection.password || '',
          trustServerCertificate: connection.trustServerCertificate
        });
        setStatus({ type: '', message: '' });
      }
    }
  };

  const handleSubmit = async (e) => {
    e.preventDefault();
    setIsLoading(true);
    setStatus({ type: '', message: '' });

    try {
      const result = await testConnection({
        ...formData,
        port: parseInt(formData.port)
      });

      if (result.success) {
        setStatus({ 
          type: 'success', 
          message: '✅ Bağlantı başarılı! Tablolar yükleniyor...' 
        });
        
        // Pass connection info to parent
        if (onConnectionSuccess) {
          onConnectionSuccess({
            ...formData,
            port: parseInt(formData.port),
            connectionId: result.connectionId
          });
        }
      } else {
        setStatus({ 
          type: 'error', 
          message: `❌ ${result.message}` 
        });
      }
    } catch (error) {
      setStatus({ 
        type: 'error', 
        message: `❌ Bağlantı hatası: ${error.message}` 
      });
    } finally {
      setIsLoading(false);
    }
  };

  const handleClear = () => {
    setFormData({
      host: '',
      port: 1433,
      database: '',
      username: '',
      password: '',
      trustServerCertificate: true
    });
    setStatus({ type: '', message: '' });
  };

  return (
    <div className="connection-form-container">
      <div className="connection-form-header">
        <h2>🔌 SQL Server Bağlantısı</h2>
        <p>Müşteri veritabanı bilgilerini girin veya kayıtlı bağlantıları kullanın</p>
      </div>

      <form onSubmit={handleSubmit} className="connection-form">
        {savedConnections.length > 0 && (
          <div className="form-group saved-connections-group">
            <label htmlFor="savedConnection">
              <i className="ti ti-bookmarks"></i> Kayıtlı Bağlantılar
            </label>
            <select
              id="savedConnection"
              value={selectedConnectionId}
              onChange={handleSavedConnectionSelect}
              className="form-select"
            >
              <option value="">-- Yeni bağlantı gir veya kayıtlı birini seç --</option>
              {savedConnections.map(conn => (
                <option key={conn.id} value={conn.id}>
                  {conn.name} ({conn.host} - {conn.database})
                </option>
              ))}
            </select>
          </div>
        )}
        <div className="form-row">
          <div className="form-group">
            <label htmlFor="host">
              <i className="ti ti-server"></i> Host / Server
            </label>
            <input
              type="text"
              id="host"
              name="host"
              value={formData.host}
              onChange={handleChange}
              placeholder="localhost veya sql.musteri.com"
              required
            />
          </div>

          <div className="form-group port-group">
            <label htmlFor="port">
              <i className="ti ti-plug"></i> Port
            </label>
            <input
              type="number"
              id="port"
              name="port"
              value={formData.port}
              onChange={handleChange}
              placeholder="1433"
              required
            />
          </div>
        </div>

        <div className="form-group">
          <label htmlFor="database">
            <i className="ti ti-database"></i> Database
          </label>
          <input
            type="text"
            id="database"
            name="database"
            value={formData.database}
            onChange={handleChange}
            placeholder="Veritabanı adı"
            required
          />
        </div>

        <div className="form-row">
          <div className="form-group">
            <label htmlFor="username">
              <i className="ti ti-user"></i> Username
            </label>
            <input
              type="text"
              id="username"
              name="username"
              value={formData.username}
              onChange={handleChange}
              placeholder="sa veya kullanıcı adı"
              required
            />
          </div>

          <div className="form-group">
            <label htmlFor="password">
              <i className="ti ti-lock"></i> Password
            </label>
            <input
              type="password"
              id="password"
              name="password"
              value={formData.password}
              onChange={handleChange}
              placeholder="••••••••"
              required
            />
          </div>
        </div>

        <div className="form-group checkbox-group">
          <label>
            <input
              type="checkbox"
              name="trustServerCertificate"
              checked={formData.trustServerCertificate}
              onChange={handleChange}
            />
            <span>Trust Server Certificate (Self-signed için gerekli)</span>
          </label>
        </div>

        {status.message && (
          <div className={`alert alert-${status.type}`}>
            {status.message}
          </div>
        )}

        <div className="form-actions">
          <button 
            type="button" 
            className="btn btn-secondary"
            onClick={handleClear}
            disabled={isLoading}
          >
            <i className="ti ti-x"></i> Temizle
          </button>
          <button 
            type="submit" 
            className="btn btn-primary"
            disabled={isLoading}
          >
            {isLoading ? (
              <>
                <span className="spinner"></span> Bağlanıyor...
              </>
            ) : (
              <>
                <i className="ti ti-plug-connected"></i> Bağlantıyı Test Et
              </>
            )}
          </button>
        </div>
      </form>
    </div>
  );
};

export default ConnectionForm;
