import React, { useState } from 'react';
import { testConnection, getDataQuality, getStatistics, getMissingData, getRelationships, startAIAnalysis, getAnalysisStatus } from '../services/dataAnalysisService';
import TableList from '../components/DataAnalysis/TableList';
import TableSchema from '../components/DataAnalysis/TableSchema';
import '../styles/SqlConnectionSettings.css';

const SqlConnectionSettings = () => {
  const [connections, setConnections] = useState([]);
  const [isFormOpen, setIsFormOpen] = useState(false);
  const [editingConnection, setEditingConnection] = useState(null);
  
  // Modal için state
  const [isModalOpen, setIsModalOpen] = useState(false);
  const [selectedConnectionForModal, setSelectedConnectionForModal] = useState(null);
  const [currentConnectionId, setCurrentConnectionId] = useState(null);
  const [selectedTable, setSelectedTable] = useState(null);
  const [selectedTablesForSave, setSelectedTablesForSave] = useState([]);
  
  // Ön analiz paneli için state
  const [showAnalysisPanel, setShowAnalysisPanel] = useState(false);
  const [selectedConnectionForAnalysis, setSelectedConnectionForAnalysis] = useState(null);
  const [analysisResults, setAnalysisResults] = useState(null);
  const [analysisLoading, setAnalysisLoading] = useState(false);
  const [activeAnalysisTab, setActiveAnalysisTab] = useState(null);
  
  // AI Settings
  const [samplingRate, setSamplingRate] = useState(100);
  const [nullHandling, setNullHandling] = useState('keep');
  const [dataFormat, setDataFormat] = useState('json');
  const [aiLoading, setAiLoading] = useState(false);
  
  const [formData, setFormData] = useState({
    name: '',
    host: '',
    port: 1433,
    database: '',
    username: '',
    password: '',
    trustServerCertificate: true
  });

  const [testStatus, setTestStatus] = useState({ type: '', message: '' });
  const [isTesting, setIsTesting] = useState(false);

  // Load connections from localStorage on mount
  React.useEffect(() => {
    const saved = localStorage.getItem('sqlConnections');
    if (saved) {
      try {
        setConnections(JSON.parse(saved));
      } catch (e) {
        console.error('Failed to load connections', e);
      }
    }
  }, []);

  // Save connections to localStorage
  const saveConnections = (newConnections) => {
    localStorage.setItem('sqlConnections', JSON.stringify(newConnections));
    setConnections(newConnections);
  };

  const handleChange = (e) => {
    const { name, value, type, checked } = e.target;
    setFormData(prev => ({
      ...prev,
      [name]: type === 'checkbox' ? checked : value
    }));
  };

  const handleTest = async () => {
    setIsTesting(true);
    setTestStatus({ type: '', message: '' });

    try {
      const result = await testConnection({
        host: formData.host,
        port: parseInt(formData.port),
        database: formData.database,
        username: formData.username,
        password: formData.password,
        trustServerCertificate: formData.trustServerCertificate
      });

      if (result.success) {
        setTestStatus({ 
          type: 'success', 
          message: '✅ Bağlantı başarılı!' 
        });
      } else {
        setTestStatus({ 
          type: 'error', 
          message: `❌ ${result.message}` 
        });
      }
    } catch (error) {
      setTestStatus({ 
        type: 'error', 
        message: `❌ Bağlantı hatası: ${error.message}` 
      });
    } finally {
      setIsTesting(false);
    }
  };

  const handleSave = () => {
    if (!formData.name || !formData.host || !formData.database || !formData.username) {
      setTestStatus({ type: 'error', message: '❌ Lütfen tüm zorunlu alanları doldurun' });
      return;
    }

    const newConnection = {
      id: editingConnection?.id || Date.now(),
      ...formData,
      createdAt: editingConnection?.createdAt || new Date().toISOString(),
      updatedAt: new Date().toISOString()
    };

    let newConnections;
    if (editingConnection) {
      newConnections = connections.map(c => 
        c.id === editingConnection.id ? newConnection : c
      );
    } else {
      newConnections = [...connections, newConnection];
    }

    saveConnections(newConnections);
    handleCancel();
    setTestStatus({ type: 'success', message: '✅ Bağlantı kaydedildi!' });
  };

  const handleEdit = (connection) => {
    setEditingConnection(connection);
    setFormData({
      name: connection.name,
      host: connection.host,
      port: connection.port,
      database: connection.database,
      username: connection.username,
      password: connection.password,
      trustServerCertificate: connection.trustServerCertificate
    });
    setIsFormOpen(true);
    setTestStatus({ type: '', message: '' });
  };

  const handleDelete = (id) => {
    if (window.confirm('Bu bağlantıyı silmek istediğinizden emin misiniz?')) {
      const newConnections = connections.filter(c => c.id !== id);
      saveConnections(newConnections);
    }
  };

  const handleCancel = () => {
    setIsFormOpen(false);
    setEditingConnection(null);
    setFormData({
      name: '',
      host: '',
      port: 1433,
      database: '',
      username: '',
      password: '',
      trustServerCertificate: true
    });
    setTestStatus({ type: '', message: '' });
  };

  // Modal handlers
  const handleOpenModal = (connection) => {
    setCurrentConnectionId(connection.id);
    setSelectedConnectionForModal({
      host: connection.host,
      port: connection.port,
      database: connection.database,
      username: connection.username,
      password: connection.password,
      trustServerCertificate: connection.trustServerCertificate
    });
    setSelectedTable(null);
    setSelectedTablesForSave(connection.selectedTables || []);
    setIsModalOpen(true);
  };

  const handleCloseModal = () => {
    setIsModalOpen(false);
    setSelectedConnectionForModal(null);
    setCurrentConnectionId(null);
    setSelectedTable(null);
    setSelectedTablesForSave([]);
  };

  const handleTableSelect = (table) => {
    setSelectedTable(table);
  };

  const handleMultiTableSelect = (tables) => {
    setSelectedTablesForSave(tables);
  };

  const handleSaveSelectedTables = () => {
    if (!currentConnectionId) return;

    const updatedConnections = connections.map(conn => {
      if (conn.id === currentConnectionId) {
        return {
          ...conn,
          selectedTables: selectedTablesForSave,
          updatedAt: new Date().toISOString()
        };
      }
      return conn;
    });

    saveConnections(updatedConnections);
    handleCloseModal();
  };

  const handleShowAnalysisPanel = (connection) => {
    setSelectedConnectionForAnalysis(connection);
    setShowAnalysisPanel(true);
  };

  const handleCloseAnalysisPanel = () => {
    setShowAnalysisPanel(false);
    setSelectedConnectionForAnalysis(null);
    setAnalysisResults(null);
    setActiveAnalysisTab(null);
  };

  const handleAnalysis = async (type) => {
    if (!selectedConnectionForAnalysis) return;

    setAnalysisLoading(true);
    setActiveAnalysisTab(type);

    try {
      const connectionInfo = {
        host: selectedConnectionForAnalysis.host,
        port: selectedConnectionForAnalysis.port,
        database: selectedConnectionForAnalysis.database,
        username: selectedConnectionForAnalysis.username,
        password: selectedConnectionForAnalysis.password,
        trustServerCertificate: selectedConnectionForAnalysis.trustServerCertificate
      };

      const tables = selectedConnectionForAnalysis.selectedTables?.map(t => t.fullName) || [];

      let result;
      switch (type) {
        case 'quality':
          result = await getDataQuality(connectionInfo, tables);
          break;
        case 'statistics':
          result = await getStatistics(connectionInfo, tables);
          break;
        case 'missing':
          result = await getMissingData(connectionInfo, tables);
          break;
        case 'relationships':
          result = await getRelationships(connectionInfo, tables);
          break;
        default:
          return;
      }

      setAnalysisResults(result);
    } catch (error) {
      setAnalysisResults({
        success: false,
        message: `Analiz başarısız: ${error.message}`
      });
    } finally {
      setAnalysisLoading(false);
    }
  };

  const handleStartAIAnalysis = async () => {
    if (!selectedConnectionForAnalysis) return;

    setAiLoading(true);

    try {
      const connectionInfo = {
        host: selectedConnectionForAnalysis.host,
        port: selectedConnectionForAnalysis.port,
        database: selectedConnectionForAnalysis.database,
        username: selectedConnectionForAnalysis.username,
        password: selectedConnectionForAnalysis.password,
        trustServerCertificate: selectedConnectionForAnalysis.trustServerCertificate
      };

      const tables = selectedConnectionForAnalysis.selectedTables?.map(t => t.fullName) || [];

      const settings = {
        samplingRate,
        nullHandling,
        dataFormat
      };

      // TODO: Gerçek userId ve companyId - şimdilik mock
      const userId = 'user-123';
      const companyId = 'company-456';

      const result = await startAIAnalysis(userId, companyId, connectionInfo, tables, settings);

      if (result.success) {
        // Active analysis olarak kaydet
        const newAnalysis = {
          requestId: result.requestId,
          database: selectedConnectionForAnalysis.database,
          host: selectedConnectionForAnalysis.host,
          tables: tables,
          status: 'processing',
          progress: 10,
          message: 'AI analizi başlatıldı...',
          startedAt: new Date().toISOString(),
          samplingRate: samplingRate,
          nullHandling: nullHandling,
          dataFormat: dataFormat,
          estimatedTime: result.estimatedTime
        };

        // localStorage'a kaydet - Dashboard tarafından polling yapılacak
        const existingAnalyses = JSON.parse(localStorage.getItem('activeAnalyses') || '[]');
        existingAnalyses.push(newAnalysis);
        localStorage.setItem('activeAnalyses', JSON.stringify(existingAnalyses));

        alert(`✅ AI Analizi başlatıldı!\n\nRequestId: ${result.requestId}\n${result.message}\n\nTahmini süre: ${result.estimatedTime}\n\n📊 Dashboard'dan takip edebilirsiniz!`);
        
        // Paneli kapat
        handleCloseAnalysisPanel();

        // Dashboard'a yönlendir
        window.location.href = '/dashboard';
      } else {
        alert('❌ AI analizi başlatılamadı: ' + result.message);
      }
    } catch (error) {
      alert('❌ Hata: ' + error.message);
      console.error('AI Analysis start error:', error);
    } finally {
      setAiLoading(false);
    }
  };

  const handleNewConnection = () => {
    setIsFormOpen(true);
    setEditingConnection(null);
    setFormData({
      name: '',
      host: '',
      port: 1433,
      database: '',
      username: '',
      password: '',
      trustServerCertificate: true
    });
    setTestStatus({ type: '', message: '' });
  };

  return (
    <div className="sql-connection-settings">
      <div className="settings-header">
        <div>
          <h1 className="page-title">
            <i className="ti ti-database-cog"></i>
            SQL Bağlantı Ayarları
          </h1>
          <p className="text-muted">Müşteri veritabanı bağlantılarını yönetin</p>
        </div>
        <button className="btn btn-primary" onClick={handleNewConnection}>
          <i className="ti ti-plus"></i>
          Yeni Bağlantı
        </button>
      </div>

      {testStatus.message && (
        <div className={`alert alert-${testStatus.type}`}>
          {testStatus.message}
        </div>
      )}

      {isFormOpen && (
        <div className="connection-form-card">
          <div className="card-header">
            <h3>
              {editingConnection ? 'Bağlantıyı Düzenle' : 'Yeni Bağlantı Ekle'}
            </h3>
          </div>
          <div className="card-body">
            <div className="form-group">
              <label htmlFor="name">
                <i className="ti ti-tag"></i> Bağlantı Adı *
              </label>
              <input
                type="text"
                id="name"
                name="name"
                value={formData.name}
                onChange={handleChange}
                placeholder="Örn: Müşteri A - Production DB"
                required
              />
            </div>

            <div className="form-row">
              <div className="form-group">
                <label htmlFor="host">
                  <i className="ti ti-server"></i> Host / Server *
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

              <div className="form-group form-group-small">
                <label htmlFor="port">
                  <i className="ti ti-plug"></i> Port *
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
                <i className="ti ti-database"></i> Database *
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
                  <i className="ti ti-user"></i> Username *
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
                  <i className="ti ti-lock"></i> Password *
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

            <div className="form-group">
              <label className="checkbox-label">
                <input
                  type="checkbox"
                  name="trustServerCertificate"
                  checked={formData.trustServerCertificate}
                  onChange={handleChange}
                />
                <span>Trust Server Certificate (Self-signed sertifikalar için)</span>
              </label>
            </div>
          </div>

          <div className="card-footer">
            <button 
              type="button" 
              className="btn btn-secondary"
              onClick={handleCancel}
            >
              <i className="ti ti-x"></i> İptal
            </button>
            <div className="btn-group">
              <button 
                type="button" 
                className="btn btn-outline-primary"
                onClick={handleTest}
                disabled={isTesting}
              >
                {isTesting ? (
                  <>
                    <span className="spinner"></span> Test Ediliyor...
                  </>
                ) : (
                  <>
                    <i className="ti ti-plug-connected"></i> Bağlantıyı Test Et
                  </>
                )}
              </button>
              <button 
                type="button" 
                className="btn btn-primary"
                onClick={handleSave}
              >
                <i className="ti ti-device-floppy"></i> Kaydet
              </button>
            </div>
          </div>
        </div>
      )}

      <div className="connections-list">
        {connections.length === 0 ? (
          <div className="empty-state">
            <i className="ti ti-database-off"></i>
            <h3>Henüz kayıtlı bağlantı yok</h3>
            <p>Yeni bir SQL bağlantısı eklemek için yukarıdaki butonu kullanın</p>
          </div>
        ) : (
          <div className="connections-grid">
            {connections.map((connection) => (
              <div key={connection.id} className="connection-card">
                <div className="connection-header">
                  <div className="connection-icon">
                    <i className="ti ti-database"></i>
                  </div>
                  <div className="connection-info">
                    <h4>{connection.name}</h4>
                    <span className="connection-database">{connection.database}</span>
                  </div>
                </div>

                <div className="connection-details">
                  <div className="detail-item">
                    <i className="ti ti-server"></i>
                    <span>{connection.host}:{connection.port}</span>
                  </div>
                  <div className="detail-item">
                    <i className="ti ti-user"></i>
                    <span>{connection.username}</span>
                  </div>
                  <div className="detail-item">
                    <i className="ti ti-clock"></i>
                    <span>{new Date(connection.updatedAt).toLocaleDateString('tr-TR')}</span>
                  </div>
                  {connection.selectedTables && connection.selectedTables.length > 0 && (
                    <div className="detail-item highlight">
                      <i className="ti ti-checks"></i>
                      <span className="badge badge-success">
                        {connection.selectedTables.length} tablo seçili
                      </span>
                    </div>
                  )}
                </div>

                <div className="connection-actions">
                  <button 
                    className="btn btn-sm btn-success"
                    onClick={() => handleOpenModal(connection)}
                  >
                    <i className="ti ti-table"></i> Tablo Seç
                  </button>
                  {connection.selectedTables && connection.selectedTables.length > 0 && (
                    <button 
                      className="btn btn-sm btn-primary"
                      onClick={() => handleShowAnalysisPanel(connection)}
                    >
                      <i className="ti ti-chart-dots"></i> Ön Analiz
                    </button>
                  )}
                  <button 
                    className="btn btn-sm btn-outline-primary"
                    onClick={() => handleEdit(connection)}
                  >
                    <i className="ti ti-edit"></i> Düzenle
                  </button>
                  <button 
                    className="btn btn-sm btn-outline-danger"
                    onClick={() => handleDelete(connection.id)}
                  >
                    <i className="ti ti-trash"></i> Sil
                  </button>
                </div>
              </div>
            ))}
          </div>
        )}
      </div>

      {/* Ön Analiz Paneli */}
      {showAnalysisPanel && selectedConnectionForAnalysis && (
        <div className="analysis-panel-overlay">
          <div className="analysis-panel">
            <div className="analysis-panel-header">
              <div className="analysis-header-info">
                <h2>
                  <i className="ti ti-chart-dots"></i>
                  Ön Analiz Paneli
                </h2>
                <p className="analysis-connection-name">{selectedConnectionForAnalysis.name}</p>
              </div>
              <button className="btn-close-panel" onClick={handleCloseAnalysisPanel}>
                <i className="ti ti-x"></i>
              </button>
            </div>

            <div className="analysis-panel-body">
              {/* Seçili Tablolar Özeti */}
              <div className="analysis-section">
                <h3>
                  <i className="ti ti-table"></i>
                  Seçili Tablolar ({selectedConnectionForAnalysis.selectedTables?.length || 0})
                </h3>
                <div className="selected-tables-list">
                  {selectedConnectionForAnalysis.selectedTables?.map((table, index) => {
                    // "dbo." önekini kaldır
                    const displayName = table.fullName.replace(/^dbo\./, '');
                    return (
                      <div key={index} className="selected-table-item">
                        <i className="ti ti-table-filled"></i>
                        <span>{displayName}</span>
                      </div>
                    );
                  })}
                </div>
              </div>

              {/* Ön Analiz Butonları */}
              <div className="analysis-section">
                <h3>
                  <i className="ti ti-settings"></i>
                  Ön Analiz İşlemleri
                </h3>
                <div className="analysis-actions-grid">
                  <button 
                    className={`analysis-action-btn ${activeAnalysisTab === 'quality' ? 'active' : ''}`}
                    onClick={() => handleAnalysis('quality')}
                    disabled={analysisLoading}
                  >
                    <i className="ti ti-heartbeat"></i>
                    <span>Veri Kalitesi</span>
                    <small>NULL, duplicate kontrolü</small>
                  </button>
                  <button 
                    className={`analysis-action-btn ${activeAnalysisTab === 'statistics' ? 'active' : ''}`}
                    onClick={() => handleAnalysis('statistics')}
                    disabled={analysisLoading}
                  >
                    <i className="ti ti-chart-bar"></i>
                    <span>İstatistiksel Özet</span>
                    <small>Min, max, avg, count</small>
                  </button>
                  <button 
                    className={`analysis-action-btn ${activeAnalysisTab === 'relationships' ? 'active' : ''}`}
                    onClick={() => handleAnalysis('relationships')}
                    disabled={analysisLoading}
                  >
                    <i className="ti ti-arrows-join"></i>
                    <span>İlişki Analizi</span>
                    <small>Foreign key tespiti</small>
                  </button>
                  <button 
                    className={`analysis-action-btn ${activeAnalysisTab === 'missing' ? 'active' : ''}`}
                    onClick={() => handleAnalysis('missing')}
                    disabled={analysisLoading}
                  >
                    <i className="ti ti-alert-triangle"></i>
                    <span>Eksik Veri</span>
                    <small>NULL değer analizi</small>
                  </button>
                </div>
                
                {analysisLoading && (
                  <div className="analysis-loading">
                    <div className="spinner-large"></div>
                    <p>Analiz yapılıyor...</p>
                  </div>
                )}

                {analysisResults && !analysisLoading && (
                  <div className="analysis-results-container">
                    {analysisResults.success ? (
                      <>
                        {activeAnalysisTab === 'quality' && (
                          <div className="quality-results">
                            <h4>📊 Veri Kalitesi Raporu</h4>
                            {analysisResults.data?.map((table, index) => (
                              <div key={index} className="result-card">
                                <div className="result-header">
                                  <span className="table-name">{table.tableName}</span>
                                  <span className={`quality-badge ${table.qualityScore >= 80 ? 'good' : table.qualityScore >= 50 ? 'medium' : 'poor'}`}>
                                    {Math.round(table.qualityScore)}%
                                  </span>
                                </div>
                                <div className="result-stats">
                                  <div className="stat">
                                    <span>Toplam Satır:</span>
                                    <strong>{table.totalRows.toLocaleString()}</strong>
                                  </div>
                                  <div className="stat">
                                    <span>NULL Değer:</span>
                                    <strong>{table.totalNulls.toLocaleString()}</strong>
                                  </div>
                                  <div className="stat">
                                    <span>Duplicate:</span>
                                    <strong>{table.duplicateRows}</strong>
                                  </div>
                                </div>
                              </div>
                            ))}
                          </div>
                        )}

                        {activeAnalysisTab === 'statistics' && (
                          <div className="statistics-results">
                            <h4>📈 İstatistiksel Özet</h4>
                            {analysisResults.data?.map((table, index) => (
                              <div key={index} className="result-card">
                                <div className="result-header">
                                  <span className="table-name">{table.tableName}</span>
                                </div>
                                <div className="result-stats">
                                  <div className="stat">
                                    <span>Satır Sayısı:</span>
                                    <strong>{table.rowCount.toLocaleString()}</strong>
                                  </div>
                                  <div className="stat">
                                    <span>Kolon Sayısı:</span>
                                    <strong>{table.columnCount}</strong>
                                  </div>
                                </div>
                                {table.numericColumns?.length > 0 && (
                                  <div className="numeric-stats">
                                    <h5>Numeric Kolonlar:</h5>
                                    {table.numericColumns.map((col, i) => (
                                      <div key={i} className="numeric-col">
                                        <strong>{col.columnName}</strong>
                                        <span>Min: {col.minValue} | Max: {col.maxValue} | Avg: {col.avgValue}</span>
                                      </div>
                                    ))}
                                  </div>
                                )}
                              </div>
                            ))}
                          </div>
                        )}

                        {activeAnalysisTab === 'relationships' && (
                          <div className="relationships-results">
                            <h4>🔗 İlişki Analizi</h4>
                            {analysisResults.data && analysisResults.data.length > 0 ? (
                              analysisResults.data.map((rel, index) => (
                                <div key={index} className="relationship-card">
                                  <div className="rel-arrow">
                                    <span>{rel.parentTable}</span>
                                    <i className="ti ti-arrow-right"></i>
                                    <span>{rel.referencedTable}</span>
                                  </div>
                                  <small>{rel.parentColumn} → {rel.referencedColumn}</small>
                                </div>
                              ))
                            ) : (
                              <p className="no-data">İlişki bulunamadı</p>
                            )}
                          </div>
                        )}

                        {activeAnalysisTab === 'missing' && (
                          <div className="missing-results">
                            <h4>⚠️ Eksik Veri Analizi</h4>
                            {analysisResults.data?.map((table, index) => (
                              <div key={index} className="result-card">
                                <div className="result-header">
                                  <span className="table-name">{table.tableName}</span>
                                  <span className="missing-badge">
                                    {Math.round(table.averageMissingPercentage)}% eksik
                                  </span>
                                </div>
                                <div className="missing-columns">
                                  {table.columns?.filter(c => c.missingCount > 0).map((col, i) => (
                                    <div key={i} className="missing-col">
                                      <span>{col.columnName}</span>
                                      <span className="missing-count">
                                        {col.missingCount} ({col.missingPercentage}%)
                                      </span>
                                    </div>
                                  ))}
                                </div>
                              </div>
                            ))}
                          </div>
                        )}
                      </>
                    ) : (
                      <div className="error-result">
                        <i className="ti ti-alert-circle"></i>
                        <p>{analysisResults.message}</p>
                      </div>
                    )}
                  </div>
                )}
              </div>

              {/* AI Ayarları */}
              <div className="analysis-section">
                <h3>
                  <i className="ti ti-adjustments"></i>
                  AI Analiz Ayarları
                </h3>
                <div className="analysis-settings">
                  <div className="setting-item">
                    <label>
                      <i className="ti ti-database"></i>
                      Örnekleme Oranı
                    </label>
                    <select 
                      className="setting-select" 
                      value={samplingRate} 
                      onChange={(e) => setSamplingRate(Number(e.target.value))}
                    >
                      <option value="100">%100 - Tüm veriler</option>
                      <option value="50">%50 - Yarısı</option>
                      <option value="25">%25 - Çeyrek</option>
                      <option value="10">%10 - On binde biri</option>
                    </select>
                  </div>
                  <div className="setting-item">
                    <label>
                      <i className="ti ti-filter"></i>
                      NULL Değer İşleme
                    </label>
                    <select 
                      className="setting-select" 
                      value={nullHandling} 
                      onChange={(e) => setNullHandling(e.target.value)}
                    >
                      <option value="keep">Olduğu gibi bırak</option>
                      <option value="remove">Satırları sil</option>
                      <option value="fill">Ortalama ile doldur</option>
                    </select>
                  </div>
                  <div className="setting-item">
                    <label>
                      <i className="ti ti-braces"></i>
                      Veri Formatı
                    </label>
                    <select 
                      className="setting-select" 
                      value={dataFormat} 
                      onChange={(e) => setDataFormat(e.target.value)}
                    >
                      <option value="json">JSON</option>
                      <option value="csv">CSV</option>
                      <option value="xml">XML</option>
                    </select>
                  </div>
                </div>
              </div>
            </div>

            <div className="analysis-panel-footer">
              <button className="btn btn-secondary" onClick={handleCloseAnalysisPanel} disabled={aiLoading}>
                <i className="ti ti-x"></i> İptal
              </button>
              <button 
                className="btn btn-ai-gradient" 
                onClick={handleStartAIAnalysis}
                disabled={aiLoading}
              >
                {aiLoading ? (
                  <>
                    <div className="spinner"></div>
                    AI'ya Gönderiliyor...
                  </>
                ) : (
                  <>
                    <i className="ti ti-brain"></i>
                    AI Analizi Başlat
                  </>
                )}
              </button>
            </div>
          </div>
        </div>
      )}

      {/* Tablo Seçim Modal */}
      {isModalOpen && (
        <div className="modal-overlay" onClick={handleCloseModal}>
          <div className="modal-container" onClick={(e) => e.stopPropagation()}>
            <div className="modal-header">
              <h2>
                <i className="ti ti-table"></i>
                Tablo Seçimi
              </h2>
              <button className="modal-close" onClick={handleCloseModal}>
                <i className="ti ti-x"></i>
              </button>
            </div>

            <div className="modal-body">
              {selectedConnectionForModal && (
                <>
                  <div className="modal-info-banner">
                    <i className="ti ti-info-circle"></i>
                    <span>Analiz etmek istediğiniz tabloları seçin ve kaydedin</span>
                    {selectedTablesForSave.length > 0 && (
                      <span className="selected-count">
                        {selectedTablesForSave.length} tablo seçildi
                      </span>
                    )}
                  </div>

                  <TableList 
                    connectionInfo={selectedConnectionForModal}
                    onTableSelect={handleMultiTableSelect}
                    multiSelect={true}
                    selectedTables={selectedTablesForSave}
                  />
                </>
              )}
            </div>

            <div className="modal-footer">
              <button className="btn btn-secondary" onClick={handleCloseModal}>
                <i className="ti ti-x"></i> Kapat
              </button>
              <button 
                className="btn btn-primary" 
                onClick={handleSaveSelectedTables}
                disabled={selectedTablesForSave.length === 0}
              >
                <i className="ti ti-check"></i> 
                Seçilenleri Kaydet ({selectedTablesForSave.length})
              </button>
            </div>
          </div>
        </div>
      )}
    </div>
  );
};

export default SqlConnectionSettings;
