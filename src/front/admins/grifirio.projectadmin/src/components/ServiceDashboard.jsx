import { useState, useEffect } from 'react';
import { 
  SERVICES, 
  checkServiceHealth, 
  getServicesByCategory,
  startDockerContainer,
  stopDockerContainer,
  restartDockerContainer,
  startDotNetService,
  stopDotNetService,
  checkDotNetServiceStatus
} from '../services/serviceManager';
import './ServiceDashboard.css';

function ServiceDashboard() {
  const [serviceStatuses, setServiceStatuses] = useState({});
  const [loading, setLoading] = useState(true);
  const [selectedCategory, setSelectedCategory] = useState('all');
  const [autoRefresh, setAutoRefresh] = useState(true);
  const [lastUpdate, setLastUpdate] = useState(null);
  const [actionLoading, setActionLoading] = useState({});

  // Servisleri kontrol et
  const checkServices = async () => {
    setLoading(true);
    const results = {};
    
    for (const [key, service] of Object.entries(SERVICES)) {
      results[key] = await checkServiceHealth(service);
    }
    
    setServiceStatuses(results);
    setLastUpdate(new Date());
    setLoading(false);
  };

  // İlk yükleme
  useEffect(() => {
    checkServices();
  }, []);

  // Otomatik yenileme (30 saniyede bir)
  useEffect(() => {
    if (!autoRefresh) return;

    const interval = setInterval(() => {
      checkServices();
    }, 30000);

    return () => clearInterval(interval);
  }, [autoRefresh]);

  // Kategoriye göre servisleri filtrele
  const getFilteredServices = () => {
    if (selectedCategory === 'all') {
      return Object.entries(SERVICES);
    }
    return Object.entries(SERVICES).filter(
      ([_, service]) => service.category === selectedCategory
    );
  };

  // Durum rengini al
  const getStatusColor = (status) => {
    switch (status) {
      case 'online': return '#28a745';
      case 'offline': return '#dc3545';
      case 'error': return '#ffc107';
      case 'timeout': return '#fd7e14';
      case 'unknown': return '#6c757d';
      default: return '#6c757d';
    }
  };

  // Durum metni
  const getStatusText = (status) => {
    switch (status) {
      case 'online': return '🟢 Online';
      case 'offline': return '🔴 Offline';
      case 'error': return '⚠️ Error';
      case 'timeout': return '⏱️ Timeout';
      case 'unknown': return '❓ Unknown';
      default: return '❓ Unknown';
    }
  };

  // Servis işlemleri
  const handleStartService = async (service) => {
    const key = service.id;
    setActionLoading(prev => ({ ...prev, [key]: 'starting' }));
    
    const result = await startDockerContainer(service.containerName);
    
    if (result.success) {
      alert(`✅ ${service.name} başarıyla başlatıldı!\n\n${result.message}`);
      // 2 saniye sonra durumu yenile
      setTimeout(() => checkServices(), 2000);
    } else {
      alert(`❌ ${service.name} başlatılamadı!\n\n${result.message}`);
    }
    
    setActionLoading(prev => ({ ...prev, [key]: null }));
  };

  const handleStopService = async (service) => {
    if (!confirm(`${service.name} servisini durdurmak istediğinizden emin misiniz?`)) {
      return;
    }
    
    const key = service.id;
    setActionLoading(prev => ({ ...prev, [key]: 'stopping' }));
    
    const result = await stopDockerContainer(service.containerName);
    
    if (result.success) {
      alert(`✅ ${service.name} başarıyla durduruldu!\n\n${result.message}`);
      setTimeout(() => checkServices(), 2000);
    } else {
      alert(`❌ ${service.name} durdurulamadı!\n\n${result.message}`);
    }
    
    setActionLoading(prev => ({ ...prev, [key]: null }));
  };

  const handleRestartService = async (service) => {
    const key = service.id;
    setActionLoading(prev => ({ ...prev, [key]: 'restarting' }));
    
    const result = await restartDockerContainer(service.containerName);
    
    if (result.success) {
      alert(`✅ ${service.name} başarıyla yeniden başlatıldı!\n\n${result.message}`);
      setTimeout(() => checkServices(), 3000);
    } else {
      alert(`❌ ${service.name} yeniden başlatılamadı!\n\n${result.message}`);
    }
    
    setActionLoading(prev => ({ ...prev, [key]: null }));
  };

  // .NET servisi başlat
  const handleStartDotNetService = async (service) => {
    const key = service.id;
    setActionLoading(prev => ({ ...prev, [key]: 'starting' }));
    
    const result = await startDotNetService(service.projectPath, service.port);
    
    if (result.success) {
      alert(`✅ ${service.name} başarıyla başlatıldı!\n\nPort: ${service.port}\n${result.message}`);
      setTimeout(() => checkServices(), 3000);
    } else {
      alert(`❌ ${service.name} başlatılamadı!\n\n${result.message}`);
    }
    
    setActionLoading(prev => ({ ...prev, [key]: null }));
  };

  // .NET servisi durdur
  const handleStopDotNetService = async (service) => {
    if (!confirm(`${service.name} servisini durdurmak istediğinizden emin misiniz?`)) {
      return;
    }
    
    const key = service.id;
    setActionLoading(prev => ({ ...prev, [key]: 'stopping' }));
    
    const result = await stopDotNetService(service.port);
    
    if (result.success) {
      alert(`✅ ${service.name} başarıyla durduruldu!\n\n${result.message}`);
      setTimeout(() => checkServices(), 2000);
    } else {
      alert(`❌ ${service.name} durdurulamadı!\n\n${result.message}`);
    }
    
    setActionLoading(prev => ({ ...prev, [key]: null }));
  };

  // Kategorileri al
  const categories = ['all', ...new Set(Object.values(SERVICES).map(s => s.category))];

  // İstatistikler
  const stats = {
    total: Object.keys(SERVICES).length,
    online: Object.values(serviceStatuses).filter(s => s.status === 'online').length,
    offline: Object.values(serviceStatuses).filter(s => s.status === 'offline').length,
    error: Object.values(serviceStatuses).filter(s => s.status === 'error' || s.status === 'timeout').length,
    unknown: Object.values(serviceStatuses).filter(s => s.status === 'unknown').length,
  };

  return (
    <div className="service-dashboard">
      {/* Header */}
      <header className="dashboard-header">
        <h1>🎛️ Grafirio Service Manager</h1>
        <p>Tüm servisleri tek bir yerden yönetin</p>
      </header>

      {/* Stats Cards */}
      <div className="stats-container">
        <div className="stat-card total">
          <div className="stat-number">{stats.total}</div>
          <div className="stat-label">Toplam Servis</div>
        </div>
        <div className="stat-card online">
          <div className="stat-number">{stats.online}</div>
          <div className="stat-label">Online</div>
        </div>
        <div className="stat-card offline">
          <div className="stat-number">{stats.offline}</div>
          <div className="stat-label">Offline</div>
        </div>
        <div className="stat-card error">
          <div className="stat-number">{stats.error}</div>
          <div className="stat-label">Hatalı</div>
        </div>
        <div className="stat-card unknown">
          <div className="stat-number">{stats.unknown}</div>
          <div className="stat-label">Bilinmeyen</div>
        </div>
      </div>

      {/* Controls */}
      <div className="controls">
        <div className="control-group">
          <label>Kategori:</label>
          <select 
            value={selectedCategory} 
            onChange={(e) => setSelectedCategory(e.target.value)}
          >
            {categories.map(cat => (
              <option key={cat} value={cat}>
                {cat === 'all' ? 'Tümü' : cat}
              </option>
            ))}
          </select>
        </div>

        <div className="control-group">
          <label>
            <input
              type="checkbox"
              checked={autoRefresh}
              onChange={(e) => setAutoRefresh(e.target.checked)}
            />
            Otomatik Yenileme (30s)
          </label>
        </div>

        <button 
          className="refresh-button" 
          onClick={checkServices}
          disabled={loading}
        >
          {loading ? '⏳ Kontrol Ediliyor...' : '🔄 Yenile'}
        </button>

        {lastUpdate && (
          <div className="last-update">
            Son Güncelleme: {lastUpdate.toLocaleTimeString('tr-TR')}
          </div>
        )}
      </div>

      {/* Services Table */}
      <div className="table-container">
        <table className="services-table">
          <thead>
            <tr>
              <th>Durum</th>
              <th>Servis Adı</th>
              <th>Kategori</th>
              <th>Port</th>
              <th>Tip</th>
              <th>Açıklama</th>
              <th>URL</th>
              <th>İşlemler</th>
            </tr>
          </thead>
          <tbody>
            {loading && Object.keys(serviceStatuses).length === 0 ? (
              <tr>
                <td colSpan="8" className="loading-cell">
                  ⏳ Servisler kontrol ediliyor...
                </td>
              </tr>
            ) : (
              getFilteredServices().map(([key, service]) => {
                const status = serviceStatuses[key] || { status: 'unknown', message: '' };
                return (
                  <tr key={key} className={`status-${status.status}`}>
                    <td>
                      <span 
                        className="status-badge"
                        style={{ backgroundColor: getStatusColor(status.status) }}
                      >
                        {getStatusText(status.status)}
                      </span>
                    </td>
                    <td className="service-name">
                      <strong>{service.name}</strong>
                    </td>
                    <td>
                      <span className="category-badge">{service.category}</span>
                    </td>
                    <td className="port-cell">
                      {service.port || '-'}
                    </td>
                    <td>
                      <span className={`type-badge type-${service.type}`}>
                        {service.type}
                      </span>
                    </td>
                    <td className="description-cell">
                      <small>{service.description}</small>
                      {status.message && status.status !== 'online' && (
                        <div className="status-message">
                          <small>💬 {status.message}</small>
                        </div>
                      )}
                    </td>
                    <td>
                      {service.url ? (
                        <a 
                          href={service.managementUI || service.url} 
                          target="_blank" 
                          rel="noopener noreferrer"
                          className="url-link"
                        >
                          🔗 Aç
                        </a>
                      ) : (
                        <span className="no-url">-</span>
                      )}
                    </td>
                    <td className="actions-cell">
                      {service.type === 'docker' && service.containerName && (
                        <>
                          <button 
                            className="action-button start"
                            title="Servisi Başlat"
                            onClick={() => handleStartService(service)}
                            disabled={actionLoading[service.id] === 'starting'}
                          >
                            {actionLoading[service.id] === 'starting' ? '⏳' : '▶️'}
                          </button>
                          <button 
                            className="action-button stop"
                            title="Servisi Durdur"
                            onClick={() => handleStopService(service)}
                            disabled={actionLoading[service.id] === 'stopping'}
                          >
                            {actionLoading[service.id] === 'stopping' ? '⏳' : '⏹️'}
                          </button>
                          <button 
                            className="action-button restart"
                            title="Servisi Yeniden Başlat"
                            onClick={() => handleRestartService(service)}
                            disabled={actionLoading[service.id] === 'restarting'}
                          >
                            {actionLoading[service.id] === 'restarting' ? '⏳' : '🔄'}
                          </button>
                        </>
                      )}
                      {service.type === 'dotnet' && service.projectPath && (
                        <>
                          <button 
                            className="action-button start"
                            title="Servisi Başlat"
                            onClick={() => handleStartDotNetService(service)}
                            disabled={actionLoading[service.id] === 'starting'}
                          >
                            {actionLoading[service.id] === 'starting' ? '⏳' : '▶️'}
                          </button>
                          <button 
                            className="action-button stop"
                            title="Servisi Durdur"
                            onClick={() => handleStopDotNetService(service)}
                            disabled={actionLoading[service.id] === 'stopping'}
                          >
                            {actionLoading[service.id] === 'stopping' ? '⏳' : '⏹️'}
                          </button>
                        </>
                      )}
                      {service.credentials && (
                        <button 
                          className="action-button info"
                          title="Credentials"
                          onClick={() => alert(
                            `Username: ${service.credentials.username}\nPassword: ${service.credentials.password}`
                          )}
                        >
                          🔑
                        </button>
                      )}
                    </td>
                  </tr>
                );
              })
            )}
          </tbody>
        </table>
      </div>

      {/* Footer */}
      <footer className="dashboard-footer">
        <p>
          💡 <strong>Not:</strong> Docker container başlatma/durdurma işlemleri için 
          backend API entegrasyonu gereklidir.
        </p>
        <p>
          📚 Daha fazla bilgi için: <a href="http://localhost:59264/test-ai-services.html" target="_blank">Test Sayfası</a>
        </p>
      </footer>
    </div>
  );
}

export default ServiceDashboard;
