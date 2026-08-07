import React, { useState } from 'react';
import { useNavigate } from 'react-router-dom';
import { testConnection, getDataQuality, getStatistics, getMissingData, getRelationships, startAIAnalysis, getAnalysisStatus, saveConnection, getSavedConnections, getConnectionById, analyzeConnectionSchema, saveSelectedTables, startPreAnalysis, getPreAnalysisState, submitPreAnalysisAnswers } from '../services/dataAnalysisService';
import TableList from '../components/DataAnalysis/TableList';
import TableSchema from '../components/DataAnalysis/TableSchema';
import '../styles/SettingsPages.css';
import '../styles/SqlConnectionSettings.css';

const SqlConnectionSettings = () => {
  const navigate = useNavigate();
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
  
  // Notification Modal
  const [notification, setNotification] = useState({ show: false, type: '', title: '', message: '', details: '' });

  // Ön analiz kapısı: bağlantı, profili çıkarılıp soruları yanıtlanana kadar
  // dashboard'da kullanılamaz. Durum sunucudan geliyor.
  const [preAnalysis, setPreAnalysis] = useState({
    open: false,
    connectionId: null,
    connectionName: '',
    running: false,
    status: null,
    questions: [],
    answers: {},
    summary: '',
    stats: null,
    consent: false,
    error: ''
  });

  const openPreAnalysis = async (connection) => {
    const connectionId = connection.savedConnectionId || connection.id;
    setPreAnalysis(p => ({
      ...p, open: true, connectionId, connectionName: connection.name,
      running: false, error: '', questions: [], answers: {}, summary: '', stats: null
    }));

    try {
      const state = await getPreAnalysisState(connectionId);
      setPreAnalysis(p => ({
        ...p,
        status: state.status,
        questions: state.questions || [],
        answers: state.answers || {},
        summary: state.summary || ''
      }));
    } catch (error) {
      setPreAnalysis(p => ({ ...p, error: error.response?.data?.error || error.message }));
    }
  };

  // Sunucu isi arka planda yapiyor ve hemen 202 donuyor; sonucu durumu
  // sorarak ogreniyoruz. Tek uzun istek gateway zaman asimina (504)
  // takiliyordu — is aslinda bitiyordu ama cevabi kimse goremiyordu.
  const runPreAnalysis = async () => {
    setPreAnalysis(p => ({ ...p, running: true, error: '', questions: [], stats: null }));
    try {
      await startPreAnalysis(preAnalysis.connectionId, preAnalysis.consent);
    } catch (error) {
      setPreAnalysis(p => ({
        ...p,
        running: false,
        error: error.response?.data?.detail || error.response?.data?.error || error.message
      }));
      return;
    }

    const connectionId = preAnalysis.connectionId;
    const startedAt = Date.now();
    const TIMEOUT_MS = 10 * 60 * 1000;

    const poll = async () => {
      if (Date.now() - startedAt > TIMEOUT_MS) {
        setPreAnalysis(p => ({
          ...p, running: false,
          error: 'Ön analiz 10 dakikada tamamlanmadı. Sunucu loglarını kontrol edin.'
        }));
        return;
      }

      try {
        const state = await getPreAnalysisState(connectionId);

        if (state.status === 'profiling') {
          setTimeout(poll, 4000);
          return;
        }

        setPreAnalysis(p => ({
          ...p,
          running: false,
          status: state.status,
          questions: state.questions || [],
          summary: state.summary || '',
          stats: state.tableCount
            ? { tables: state.tableCount, columns: state.columnCount, sampled: state.sampledColumnCount }
            : null,
          error: state.status === 'failed'
            ? 'Ön analiz başarısız oldu. Sunucu loglarında sebebi yazıyor.'
            : ''
        }));
      } catch (error) {
        setPreAnalysis(p => ({
          ...p, running: false,
          error: error.response?.data?.error || error.message
        }));
      }
    };

    setTimeout(poll, 3000);
  };

  const submitAnswers = async () => {
    setPreAnalysis(p => ({ ...p, running: true, error: '' }));
    try {
      await submitPreAnalysisAnswers(preAnalysis.connectionId, preAnalysis.answers);
      setPreAnalysis(p => ({ ...p, running: false, status: 'ready' }));
      setNotification({
        show: true, type: 'success', title: 'Bağlantı hazır',
        message: 'Ön analiz tamamlandı. Bu bağlantı artık dashboard\'da grafik üretebilir.',
        details: ''
      });
    } catch (error) {
      setPreAnalysis(p => ({
        ...p, running: false,
        error: error.response?.data?.error || error.message
      }));
    }
  };
  
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
      // TODO: Gerçek userId - şimdilik mock
      const userId = 'user-123';

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
        ? { ...conn, selectedTables: selectedTablesForSave, preAnalysisStatus: 'pending', updatedAt: new Date().toISOString() }
        : conn
    );

    saveConnections(updatedConnections);
    handleCloseModal();

    setNotification({
      show: true,
      type: 'success',
      title: 'Tablolar kaydedildi',
      message: `${selectedTablesForSave.length} tablo seçildi. Bağlantının kullanılabilmesi için "Ön Analiz" çalıştırın.`,
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

  const handleStartAIAnalysis = async () => {
    if (!selectedConnectionForAnalysis) return;

    // Debug: Connection bilgilerini loglayalım
    console.log('🔍 Starting AI Analysis with connection:', {
      name: selectedConnectionForAnalysis.name,
      savedConnectionId: selectedConnectionForAnalysis.savedConnectionId,
      id: selectedConnectionForAnalysis.id,
      fullConnection: selectedConnectionForAnalysis
    });

    // Eğer connection'ın savedConnectionId varsa onu kullan, yoksa hata ver
    if (!selectedConnectionForAnalysis.savedConnectionId) {
      console.error('❌ No savedConnectionId found in connection:', selectedConnectionForAnalysis);
      setNotification({
        show: true,
        type: 'error',
        title: '❌ Hata',
        message: 'Bağlantı ID\'si bulunamadı! Lütfen sayfayı yenileyin ve tekrar deneyin.',
        details: `Connection: ${selectedConnectionForAnalysis.name}, ID: ${selectedConnectionForAnalysis.id}, SavedID: ${selectedConnectionForAnalysis.savedConnectionId}`
      });
      return;
    }

    setAiLoading(true);

    try {
      const tables = selectedConnectionForAnalysis.selectedTables?.map(t => t.fullName) || [];

      const settings = {
        samplingRate,
        nullHandling,
        dataFormat
      };

      // TODO: Gerçek userId ve companyId - şimdilik mock
      const userId = 'user-123';
      const companyId = 'company-456';

      const result = await startAIAnalysis(
        userId, 
        companyId, 
        selectedConnectionForAnalysis.savedConnectionId, // connectionId gönder
        tables, 
        settings
      );

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

        setNotification({
          show: true,
          type: 'success',
          title: '✅ AI Analizi Başlatıldı!',
          message: result.message,
          details: `Request ID: ${result.requestId}\nTahmini süre: ${result.estimatedTime}\n\n📊 Dashboard'dan takip edebilirsiniz!`
        });
        
        // Paneli kapat
        setTimeout(() => {
          handleCloseAnalysisPanel();
          // Dashboard'a yönlendir
          navigate('/dashboard');
        }, 2000);
      } else {
        setNotification({
          show: true,
          type: 'error',
          title: '❌ AI Analizi Başlatılamadı',
          message: result.message || 'Bilinmeyen hata',
          details: ''
        });
      }
    } catch (error) {
      setNotification({
        show: true,
        type: 'error',
        title: '❌ Hata',
        message: error.message || 'AI analizi başlatılamadı',
        details: error.stack || ''
      });
      console.error('AI Analysis start error:', error);
    } finally {
      setAiLoading(false);
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

  const handleAnalyzeSchema = async (connection) => {
    const connId = connection.savedConnectionId || connection.id;
    if (!connId) {
      setNotification({
        show: true,
        type: 'error',
        title: 'Hata',
        message: 'Bağlantı ID bulunamadı'
      });
      return;
    }

    // Set analyzing state
    setConnections(prev => prev.map(c => 
      c.id === connection.id ? { ...c, _analyzing: true } : c
    ));

    try {
      const result = await analyzeConnectionSchema(connId);
      if (result.success) {
        // Dashboard'a analiz kartı ekle
        const entry = {
          requestId: connId,
          database: connection.database || connection.name,
          tables: connection.selectedTables || [],
          status: 'completed',
          completedAt: new Date().toISOString()
        };
        const existing = JSON.parse(localStorage.getItem('activeAnalyses') || '[]');
        const filtered = existing.filter(a => a.requestId !== connId);
        filtered.push(entry);
        localStorage.setItem('activeAnalyses', JSON.stringify(filtered));

        setNotification({
          show: true,
          type: 'success',
          title: 'Analiz Tamamlandı!',
          message: 'Analiz Dashboard\'a eklendi. Kanvasa geçmek için Dashboard\'daki karta çift tıklayın.',
          details: result.schemaSummary || ''
        });

        setTimeout(() => navigate('/'), 1500);
      } else {
        setNotification({
          show: true,
          type: 'error',
          title: 'Analiz Başarısız',
          message: result.error || 'Schema analizi sırasında hata oluştu'
        });
      }
    } catch (err) {
      setNotification({
        show: true,
        type: 'error',
        title: 'Hata',
        message: err.response?.data?.detail || err.message
      });
    } finally {
      setConnections(prev => prev.map(c => 
        c.id === connection.id ? { ...c, _analyzing: false } : c
      ));
    }
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
                  <button
                    className="gf-btn gf-btn--sm"
                    onClick={() => handleAnalyzeSchema(connection)}
                    disabled={connection._analyzing}
                  >
                    {connection._analyzing ? (
                      <><span className="gf-spinner"></span> Analiz Ediliyor...</>
                    ) : (
                      <><i className="ti ti-sparkles"></i> Analiz Et</>
                    )}
                  </button>
                  <button
                    className="gf-btn gf-btn--sm"
                    onClick={() => navigate(`/canvas?connectionId=${connection.savedConnectionId || connection.id}`)}
                  >
                    <i className="ti ti-brain"></i> AI Sorgulama
                  </button>
                  {/* On analiz artik bekletici kapi: baglantiyi kullanilabilir
                      hale getiren adim bu. Tablo secilmeden calismaz. */}
                  <button
                    className="gf-btn gf-btn--sm gf-btn--primary"
                    onClick={() => openPreAnalysis(connection)}
                  >
                    <i className="ti ti-chart-dots"></i> Ön Analiz
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
              <button className="gf-btn gf-btn--ghost" onClick={handleCloseAnalysisPanel} disabled={aiLoading}>
                <i className="ti ti-x"></i> İptal
              </button>
              <button
                className="gf-btn gf-btn--primary"
                onClick={handleStartAIAnalysis}
                disabled={aiLoading}
              >
                {aiLoading ? (
                  <>
                    <div className="gf-spinner"></div>
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

      {/* Ön Analiz — bekletici kapı.
          Bağlantı burada "anlaşılıyor": seçili tabloların profili çıkarılıyor,
          semantik sözlük üretiliyor, sistemin emin olamadığı şeyler bir kez
          soruluyor. Ancak bundan sonra dashboard'da kullanılabilir. */}
      {preAnalysis.open && (
        <div className="modal-overlay" onClick={() => setPreAnalysis(p => ({ ...p, open: false }))}>
          <div className="modal-container" onClick={(e) => e.stopPropagation()}>
            <div className="modal-header">
              <h2><i className="ti ti-chart-dots"></i> Ön Analiz — {preAnalysis.connectionName}</h2>
              <button className="modal-close" onClick={() => setPreAnalysis(p => ({ ...p, open: false }))}>
                <i className="ti ti-x"></i>
              </button>
            </div>

            <div className="modal-body">
              {preAnalysis.error && (
                <div className="gf-alert gf-alert--danger" style={{ marginBottom: 16 }}>
                  <i className="ti ti-alert-circle"></i> {preAnalysis.error}
                </div>
              )}

              {preAnalysis.status === 'ready' && (
                <div className="gf-alert gf-alert--success" style={{ marginBottom: 16 }}>
                  <i className="ti ti-check"></i> Bu bağlantı hazır — dashboard'da grafik üretebilir.
                </div>
              )}

              {!preAnalysis.running && preAnalysis.status !== 'ready' && preAnalysis.questions.length === 0 && (
                <>
                  <p className="gf-hint" style={{ marginBottom: 16 }}>
                    Seçili tabloların yapısı okunacak, kolonların ne anlama geldiği çıkarılacak.
                    Böylece soru sorarken kolon adı bilmeniz gerekmez.
                  </p>

                  <label className="gf-checkbox" style={{ marginBottom: 16, alignItems: 'flex-start' }}>
                    <input
                      type="checkbox"
                      checked={preAnalysis.consent}
                      onChange={(e) => setPreAnalysis(p => ({ ...p, consent: e.target.checked }))}
                    />
                    <span>
                      Serbest metin kolonlarından (firma adı, ürün adı gibi) örnek değer okunmasına
                      izin veriyorum. <strong>Kimlik no, telefon, e-posta ve adres hiçbir koşulda
                      okunmaz.</strong> İzin vermezseniz eşleştirme yalnızca kolon adı ve tipe
                      dayanır, doğruluk düşebilir.
                    </span>
                  </label>

                  <button className="gf-btn gf-btn--primary" onClick={runPreAnalysis}>
                    <i className="ti ti-player-play"></i> Analizi başlat
                  </button>
                </>
              )}

              {preAnalysis.running && (
                <div className="analysis-loading">
                  <div className="spinner-large"></div>
                  <p>Tablolar okunuyor ve anlamlandırılıyor… Bu işlem birkaç dakika sürebilir.</p>
                </div>
              )}

              {preAnalysis.stats && (
                <div className="gf-alert" style={{ marginBottom: 16 }}>
                  {preAnalysis.stats.tables} tablo · {preAnalysis.stats.columns} kolon ·
                  {' '}{preAnalysis.stats.sampled} kolondan örnek değer okundu
                </div>
              )}

              {preAnalysis.summary && (
                <p className="gf-hint" style={{ marginBottom: 16 }}>{preAnalysis.summary}</p>
              )}

              {preAnalysis.questions.length > 0 && preAnalysis.status !== 'ready' && (
                <>
                  <h3 style={{ marginBottom: 12 }}>Birkaç şeyden emin olamadım</h3>
                  <p className="gf-hint" style={{ marginBottom: 16 }}>
                    Bunları bir kez yanıtlamanız yeterli; her soruda tekrar sorulmaz.
                  </p>

                  {preAnalysis.questions.map(q => (
                    <div key={q.id} className="analysis-section" style={{ marginBottom: 16 }}>
                      {q.column && (
                        <div className="gf-badge" style={{ marginBottom: 6 }}>
                          {q.table ? `${q.table}.${q.column}` : q.column}
                        </div>
                      )}
                      <p style={{ marginBottom: 8 }}>{q.question}</p>
                      <div style={{ display: 'flex', gap: 8, flexWrap: 'wrap' }}>
                        {(q.options?.length ? q.options : ['Evet', 'Hayır', 'Emin değilim']).map(opt => (
                          <button
                            key={opt}
                            className={`gf-btn gf-btn--sm ${preAnalysis.answers[q.id] === opt ? 'gf-btn--primary' : ''}`}
                            onClick={() => setPreAnalysis(p => ({
                              ...p, answers: { ...p.answers, [q.id]: opt }
                            }))}
                          >
                            {opt}
                          </button>
                        ))}
                      </div>
                    </div>
                  ))}
                </>
              )}
            </div>

            <div className="modal-footer">
              <button className="gf-btn gf-btn--ghost" onClick={() => setPreAnalysis(p => ({ ...p, open: false }))}>
                Kapat
              </button>
              {preAnalysis.questions.length > 0 && preAnalysis.status !== 'ready' && (
                <button
                  className="gf-btn gf-btn--primary"
                  onClick={submitAnswers}
                  disabled={preAnalysis.running || Object.keys(preAnalysis.answers).length < preAnalysis.questions.length}
                >
                  <i className="ti ti-check"></i> Yanıtları kaydet ve bitir
                </button>
              )}
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
