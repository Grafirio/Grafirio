import React, { useState } from 'react';
import { useNavigate } from 'react-router-dom';
import {
  testConnection, saveConnection, getSavedConnections, getConnectionById,
  getDataQuality, getStatistics, getMissingData, getRelationships,
  saveSelectedTables,
} from '../services/dataAnalysisService';
import TableList from '../components/DataAnalysis/TableList';
import { useAnalysis } from '../contexts/AnalysisContext';
import '../styles/SettingsPages.css';
import '../styles/SqlConnectionSettings.css';

const SqlConnectionSettings = () => {
  const navigate = useNavigate();
  // Analiz durumu uygulama seviyesinde: sayfa degisince kaybolmasin.
  const { openFor: openAnalysis } = useAnalysis();
  const [connections, setConnections] = useState([]);
  const [isLoadingConnections, setIsLoadingConnections] = useState(true);
  const [loadError, setLoadError] = useState('');
  const [isFormOpen, setIsFormOpen] = useState(false);
  const [editingConnection, setEditingConnection] = useState(null);
  const [savedConnectionId, setSavedConnectionId] = useState(null); // Database'e kaydedilen connection ID
  
  // Modal için state
  const [isModalOpen, setIsModalOpen] = useState(false);
  const [selectedConnectionForModal, setSelectedConnectionForModal] = useState(null);
  const [currentConnectionId, setCurrentConnectionId] = useState(null);
  const [selectedTablesForSave, setSelectedTablesForSave] = useState([]);
  
  // Veri kalitesi paneli için state
  const [showAnalysisPanel, setShowAnalysisPanel] = useState(false);
  const [selectedConnectionForAnalysis, setSelectedConnectionForAnalysis] = useState(null);
  const [analysisResults, setAnalysisResults] = useState(null);
  const [analysisLoading, setAnalysisLoading] = useState(false);
  const [activeAnalysisTab, setActiveAnalysisTab] = useState(null);
  
  // Notification Modal
  const [notification, setNotification] = useState({ show: false, type: '', title: '', message: '', details: '' });

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

  // Load connections from database
  const loadConnections = async () => {
    setIsLoadingConnections(true);
    setLoadError('');
    try {
      const result = await getSavedConnections();

      if (result.success && result.connections) {
        // API'den gelen bağlantıları localStorage formatına çevir
        const formattedConnections = result.connections.map(conn => {
          return {
            id: conn.id,
            savedConnectionId: conn.id, // Database'deki ID'yi sakla
            name: conn.name,
            host: conn.host,
            port: conn.port,
            database: conn.database,
            username: conn.username,
            password: '', // Şifre frontend'de saklanmaz
            trustServerCertificate: conn.trustServerCertificate,
            createdAt: conn.createdAt,
            updatedAt: conn.updatedAt,
            lastConnectedAt: conn.lastConnectedAt,
            selectedTables: [] // Tables localStorage'da kalabilir veya ayrı bir API
          };
        });
        
        setConnections(formattedConnections);

        // Backward compatibility için localStorage'a da kaydet
        localStorage.setItem('sqlConnections', JSON.stringify(formattedConnections));
      } else {
        setConnections([]);
      }
    } catch (error) {
      console.error('Failed to load connections from database:', error);

      // Database hatası varsa fallback olarak localStorage'dan yükle
      const saved = localStorage.getItem('sqlConnections');
      let recovered = false;
      if (saved) {
        try {
          setConnections(JSON.parse(saved));
          recovered = true;
        } catch (e) {
          console.error('Failed to load connections from localStorage', e);
        }
      }
      if (!recovered) {
        setLoadError('Bağlantılar yüklenemedi. Lütfen tekrar deneyin.');
      }
    } finally {
      setIsLoadingConnections(false);
    }
  };

  // Load connections on mount
  React.useEffect(() => {
    loadConnections();
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
        
        // Test başarılıysa bağlantıyı database'e kaydet
        try {
          // TODO: Gerçek userId ve companyId - şimdilik mock
          const userId = 'user-123';
          const companyId = 'company-456';
          
          const saveResult = await saveConnection(
            userId,
            companyId,
            formData.name || `${formData.host}-${formData.database}`,
            {
              host: formData.host,
              port: parseInt(formData.port),
              database: formData.database,
              username: formData.username,
              password: formData.password,
              trustServerCertificate: formData.trustServerCertificate
            }
          );
          
          if (saveResult.success) {
            setSavedConnectionId(saveResult.connectionId);
            const message = saveResult.message || 'Bağlantı kaydedildi!';
            setTestStatus({ 
              type: 'success', 
              message: `✅ Bağlantı başarılı ve güvenli şekilde ${message.toLowerCase()}` 
            });
            
            // Bağlantılar listesini yeniden yükle
            await loadConnections();
          }
        } catch (saveError) {
          console.error('Connection save error:', saveError);
          // Test başarılı ama kayıt başarısız - kullanıcıya bilgi ver ama devam et
          setTestStatus({ 
            type: 'warning', 
            message: '✅ Bağlantı başarılı! (Ancak kaydedilemedi: ' + saveError.message + ')' 
          });
        }
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

  const handleEdit = async (connection) => {
    setEditingConnection(connection);
    
    // Şifreyi decrypt edip al
    let decryptedPassword = '';
    try {
      if (connection.savedConnectionId || connection.id) {
        const result = await getConnectionById(connection.savedConnectionId || connection.id);
        if (result.success && result.connection && result.connection.password) {
          decryptedPassword = result.connection.password;
        }
      }
    } catch (error) {
      console.error('Failed to fetch decrypted password:', error);
    }
    
    setFormData({
      name: connection.name,
      host: connection.host,
      port: connection.port,
      database: connection.database,
      username: connection.username,
      password: decryptedPassword, // Decrypt edilmiş şifre
      trustServerCertificate: connection.trustServerCertificate
    });
    setSavedConnectionId(connection.savedConnectionId || connection.id); // Edit modunda connection ID'yi sakla
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
    setSavedConnectionId(null); // Saved connection ID'yi temizle
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

  // Listeleme uçları şifreyi taşımaz (loadConnections onu boş bırakır), ama tablo
  // listesi ve ön analiz doğrudan SQL'e bağlandığı için gerçek şifreye ihtiyaç
  // duyar. Boş şifreyle gidildiğinde sunucu "Login failed" döndürüyordu.
  const resolveCredentials = async (connection) => {
    if (connection.password) return connection;

    const id = connection.savedConnectionId || connection.id;
    if (!id) return connection;

    try {
      const result = await getConnectionById(id);
      if (result?.success && result.connection?.password) {
        return { ...connection, password: result.connection.password };
      }
    } catch (error) {
      console.error('Failed to resolve connection password:', error);
    }
    return connection;
  };

  const warnMissingPassword = () => {
    setNotification({
      show: true,
      type: 'error',
      title: 'Bağlantı şifresi alınamadı',
      message: 'Kayıtlı şifre çözülemedi. Bağlantıyı düzenleyip şifreyi yeniden kaydedin.',
      details: ''
    });
  };

  // Modal handlers
  const handleOpenModal = async (connection) => {
    const resolved = await resolveCredentials(connection);
    if (!resolved.password) {
      warnMissingPassword();
      return;
    }

    setCurrentConnectionId(connection.id);
    setSelectedConnectionForModal({
      host: resolved.host,
      port: resolved.port,
      database: resolved.database,
      username: resolved.username,
      password: resolved.password,
      trustServerCertificate: resolved.trustServerCertificate
    });
    setSelectedTablesForSave(connection.selectedTables || []);
    setIsModalOpen(true);
  };

  const handleCloseModal = () => {
    setIsModalOpen(false);
    setSelectedConnectionForModal(null);
    setCurrentConnectionId(null);
    setSelectedTablesForSave([]);
  };

  const handleMultiTableSelect = (tables) => {
    setSelectedTablesForSave(tables);
  };

  // Secim artik sunucuya kaydediliyor. Onceden yalnizca localStorage'daydi;
  // sunucu hangi tablolarin secildigini bilmedigi icin "yalnizca secili
  // tablolar islenir" kurali uygulanamiyordu. localStorage kopyasi arayuzun
  // anlik gosterimi icin korunuyor, ama artik dogru kaynak sunucu.
  const handleSaveSelectedTables = async () => {
    if (!currentConnectionId) return;

    const connectionId = selectedConnectionForModal?.savedConnectionId
      || connections.find(c => c.id === currentConnectionId)?.savedConnectionId
      || currentConnectionId;

    try {
      await saveSelectedTables(
        connectionId,
        selectedTablesForSave.map(t => t.fullName || t.name).filter(Boolean)
      );
    } catch (error) {
      setNotification({
        show: true,
        type: 'error',
        title: 'Tablo seçimi kaydedilemedi',
        message: error.response?.data?.error || error.message,
        details: ''
      });
      return;
    }

    const updatedConnections = connections.map(conn =>
      conn.id === currentConnectionId
        ? { ...conn, selectedTables: selectedTablesForSave, analysisStatus: 'none', updatedAt: new Date().toISOString() }
        : conn
    );

    saveConnections(updatedConnections);
    handleCloseModal();

    setNotification({
      show: true,
      type: 'success',
      title: 'Tablolar kaydedildi',
      // Seçim değişince sunucu eski analizi geçersiz kılıyor; kullanıcı bunu
      // bilmezse "hazırdı, ne oldu" diye takılıyor.
      message: `${selectedTablesForSave.length} tablo seçildi. Seçim değiştiği için önceki analiz geçersiz oldu — "Analiz Et" çalıştırın.`,
      details: ''
    });
  };

  const handleShowAnalysisPanel = async (connection) => {
    // Ön analiz uçları da doğrudan SQL'e bağlanır — şifreyi önce çöz.
    const resolved = await resolveCredentials(connection);
    if (!resolved.password) {
      warnMissingPassword();
      return;
    }

    setSelectedConnectionForAnalysis({
      ...resolved,
      savedConnectionId: connection.savedConnectionId || connection.id || savedConnectionId // Try multiple sources
    });
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

  const handleNewConnection = () => {
    setIsFormOpen(true);
    setEditingConnection(null);
    setSavedConnectionId(null); // Saved connection ID'yi temizle
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
      {/* Baslik, diger ayar sayfalariyla ayni kaliptan: eyebrow + buyuk
          baslik + aciklama. Onceki hali Tabler'in kucuk sayfa basligiydi ve
          menuden gecerken tek basina farkli bir uygulama gibi duruyordu. */}
      <div className="st-head" style={{ marginBottom: 24 }}>
        <div>
          <p className="st-eyebrow">Ayarlar · Bağlantı</p>
          <h1 style={{ fontFamily: 'var(--gf-font-head)', fontSize: 34, letterSpacing: '-0.025em' }}>
            SQL Bağlantı Ayarları
          </h1>
          <p className="st-lead">
            Veritabanı bağlantılarınızı yönetin, tablo seçin ve doğrudan analiz başlatın.
          </p>
        </div>
        <div className="st-head-actions">
          <button className="st-btn" onClick={handleNewConnection}>
            + Yeni Bağlantı
          </button>
        </div>
      </div>

      {testStatus.message && (
        <div className={`gf-alert gf-alert--${testStatus.type === 'error' ? 'danger' : testStatus.type}`}>
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
              className="gf-btn gf-btn--ghost"
              onClick={handleCancel}
            >
              <i className="ti ti-x"></i> İptal
            </button>
            <div className="btn-group">
              <button
                type="button"
                className="gf-btn"
                onClick={handleTest}
                disabled={isTesting}
              >
                {isTesting ? (
                  <>
                    <span className="gf-spinner"></span> Test Ediliyor...
                  </>
                ) : (
                  <>
                    <i className="ti ti-plug-connected"></i> Bağlantıyı Test Et
                  </>
                )}
              </button>
              <button
                type="button"
                className="gf-btn gf-btn--primary"
                onClick={handleSave}
              >
                <i className="ti ti-device-floppy"></i> Kaydet
              </button>
            </div>
          </div>
        </div>
      )}

      <div className="connections-list">
        {isLoadingConnections ? (
          <div className="connections-grid" aria-busy="true" aria-label="Bağlantılar yükleniyor">
            {[0, 1, 2].map((i) => (
              <div key={i} className="connection-card connection-card--skeleton">
                <div className="connection-header">
                  <div className="gf-skeleton connection-skeleton__icon"></div>
                  <div className="connection-skeleton__heading">
                    <div className="gf-skeleton gf-skeleton--title"></div>
                    <div className="gf-skeleton gf-skeleton--text gf-skeleton--short"></div>
                  </div>
                </div>
                <div className="connection-details">
                  <div className="gf-skeleton gf-skeleton--line"></div>
                  <div className="gf-skeleton gf-skeleton--line gf-skeleton--short"></div>
                  <div className="gf-skeleton gf-skeleton--line gf-skeleton--short"></div>
                </div>
              </div>
            ))}
          </div>
        ) : loadError ? (
          <div className="gf-alert gf-alert--danger">
            <i className="ti ti-alert-circle"></i>
            <div className="gf-stack" style={{ gap: 'var(--space-3)' }}>
              <span>{loadError}</span>
              <button className="gf-btn gf-btn--sm" onClick={loadConnections}>
                <i className="ti ti-refresh"></i> Tekrar dene
              </button>
            </div>
          </div>
        ) : connections.length === 0 ? (
          <div className="gf-empty">
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
                    className="gf-btn gf-btn--sm"
                    onClick={() => handleOpenModal(connection)}
                  >
                    <i className="ti ti-table"></i> Tablo Seç
                  </button>
                  {/* Bağlantıyı sorgulanabilir hale getiren tek adım. Tablo
                      seçilmeden çalışmaz; sonunda bağlantı `ready` olur. */}
                  <button
                    className="gf-btn gf-btn--sm gf-btn--primary"
                    onClick={() => openAnalysis(connection)}
                  >
                    <i className="ti ti-sparkles"></i> Analiz Et
                  </button>
                  <button
                    className="gf-btn gf-btn--sm"
                    onClick={() => navigate(`/canvas?connectionId=${connection.savedConnectionId || connection.id}`)}
                  >
                    <i className="ti ti-brain"></i> AI Sorgulama
                  </button>
                  {connection.selectedTables && connection.selectedTables.length > 0 && (
                    <button
                      className="gf-btn gf-btn--sm"
                      onClick={() => handleShowAnalysisPanel(connection)}
                    >
                      <i className="ti ti-heartbeat"></i> Veri Kalitesi
                    </button>
                  )}
                  <button
                    className="gf-btn gf-btn--sm gf-btn--ghost"
                    onClick={() => handleEdit(connection)}
                  >
                    <i className="ti ti-edit"></i> Düzenle
                  </button>
                  <button
                    className="gf-btn gf-btn--sm gf-btn--danger"
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

            </div>

            {/* "AI Analiz Ayarları" bölümü kaldırıldı: örnekleme oranı, NULL
                işleme ve veri formatı seçimleri Django hattına gidiyordu ve o
                hat sökülmüştü — seçim yapılıyor ama hiçbir şeyi etkilemiyordu. */}
            <div className="analysis-panel-footer">
              <button className="gf-btn gf-btn--ghost" onClick={handleCloseAnalysisPanel}>
                <i className="ti ti-x"></i> Kapat
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
              <button className="gf-btn gf-btn--ghost" onClick={handleCloseModal}>
                <i className="ti ti-x"></i> Kapat
              </button>
              <button
                className="gf-btn gf-btn--primary"
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

      {/* Notification Modal */}
      {notification.show && (
        <div className="notification-overlay" onClick={() => setNotification({ ...notification, show: false })}>
          <div className="notification-modal" onClick={(e) => e.stopPropagation()}>
            <div className={`notification-header notification-${notification.type}`}>
              <h3>{notification.title}</h3>
              <button className="notification-close" onClick={() => setNotification({ ...notification, show: false })}>
                <i className="ti ti-x"></i>
              </button>
            </div>
            <div className="notification-body">
              <p className="notification-message">{notification.message}</p>
              {notification.details && (
                <pre className="notification-details">{notification.details}</pre>
              )}
            </div>
            <div className="notification-footer">
              <button
                className="gf-btn gf-btn--primary"
                onClick={() => setNotification({ ...notification, show: false })}
              >
                Tamam
              </button>
            </div>
          </div>
        </div>
      )}
    </div>
  );
};

export default SqlConnectionSettings;
