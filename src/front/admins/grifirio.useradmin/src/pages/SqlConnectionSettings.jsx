import React, { useState, useEffect } from 'react';
import { useNavigate } from 'react-router-dom';
import {
  testConnection, saveConnection, updateConnection, deleteConnection,
  getSavedConnections, getConnectionById,
  getDataQuality, getStatistics, getMissingData, getRelationships,
  saveSelectedTables, getSelectedTables,
  getBridges,
  revokeBridge, getBridgeInstallerInfo, bridgeInstallerUrl,
} from '../services/dataAnalysisService';
import TableList from '../components/DataAnalysis/TableList';
import { useAnalysis } from '../contexts/AnalysisContext';
import '../styles/SettingsPages.css';
import '../styles/SqlConnectionSettings.css';

// embedded: Veri kaynaklari sayfasi (DataSourcesPage) bu bileseni "Baglantilar"
// sekmesinin icinde gosteriyor ve kendi sayfa basligini kendisi ciziyor; bu
// durumda burasi kendi ".st-head" basligini tekrar cizmez.
const SqlConnectionSettings = ({ embedded = false } = {}) => {
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

  /* ── Bridge ────────────────────────────────────────────────────────
     Kurumsal veritabanlarının çoğu firewall arkasında ve buluttan
     erişilemiyor. Bridge yönü çeviriyor: bağlantıyı müşterinin kendi
     sunucusu dışarı doğru kurar. Panelin buradaki işi yalnızca kurulumu
     başlatmak ve durumu göstermek: hangi bağlantının hangi makineden
     okunacağı SORULMUYOR — çevrimiçi bir bridge varsa hepsi oradan
     okunuyor.                                                           */
  const [bridges, setBridges] = useState([]);
  const [bridgeError, setBridgeError] = useState('');
  const [installer, setInstaller] = useState(null);
  const [enrollment, setEnrollment] = useState(null);

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

  /**
   * Bridge listesi ve bağlantı eşlemeleri.
   *
   * Hata sayfayı düşürmüyor: bridge kullanmayan bir şirkette bu uçların
   * çalışmaması, bağlantı yönetimini engellememeli. Sebep ayrı bir satırda
   * gösteriliyor — sessizce boş bir liste, "hiç bridge'im yok" ile
   * "listeyi alamadım"ı ayırt edilemez yapardı.
   */
  /**
   * Kurulum dosyasının bu ortamda yayınlanıp yayınlanmadığı.
   *
   * Sorulmadan bir indirme düğmesi koymak, tıklanana kadar çalışıyor görünen
   * bir arayüz demek olurdu. Hata sayfayı düşürmüyor: dosya yoksa düğme
   * yerine ne yapılacağını anlatan bir cümle çıkıyor.
   */
  const loadInstaller = async () => {
    try {
      setInstaller(await getBridgeInstallerInfo());
    } catch (error) {
      console.error('Kurulum dosyası bilgisi alınamadı:', error);
      setInstaller({ available: false });
    }
  };

  const loadBridges = async () => {
    setBridgeError('');
    try {
      const list = await getBridges();
      setBridges(Array.isArray(list) ? list : []);
    } catch (error) {
      console.error('Bridge bilgileri alınamadı:', error);
      setBridges([]);
      setBridgeError(
        error?.response?.data?.error ?? 'Bridge bilgileri alınamadı.'
      );
    }
  };

  useEffect(() => {
    loadBridges();
  }, []);

  /**
   * Şirketin çevrimiçi masaüstü uygulaması; yoksa null.
   *
   * Yol artık bağlantı başına SEÇİLMİYOR, türetiliyor: çevrimiçi bir bridge
   * varsa şirketin bütün bağlantıları oradan okunuyor. Kullanıcıya "bu
   * bağlantı hangi makineden okunsun" diye sormanın karşılığı yoktu —
   * masaüstü uygulamasını kuran biri zaten veritabanına buluttan
   * ulaşılamadığı için kuruyor.
   */
  const onlineBridge = () => bridges.find((b) => b.online) ?? null;

  /**
   * Bir yükleme hatasını, kullanıcının ne yapacağını bilebileceği bir cümleye
   * çevirir. Durum kodu da yazılıyor: destek istendiğinde sorulacak ilk şey o.
   */
  const describeLoadFailure = (error) => {
    const status = error?.response?.status;
    const serverSaid = error?.response?.data?.error;

    if (serverSaid) return `${serverSaid} (HTTP ${status})`;

    switch (status) {
      case 401:
        return 'Oturumunuz düşmüş görünüyor. Çıkış yapıp tekrar giriş yapın. (HTTP 401)';
      case 403:
        return 'Veri kaynaklarını görme yetkiniz yok. Şirket yöneticinizle görüşün. (HTTP 403)';
      case undefined:
        return `Sunucuya ulaşılamadı. İnternet bağlantınızı kontrol edin. (${error?.message ?? 'ağ hatası'})`;
      default:
        return `Bağlantılar yüklenemedi — sunucu ${status} döndü. Lütfen tekrar deneyin.`;
    }
  };

  /**
   * Bağlantılar sunucudan okunuyor — ve yalnızca sunucudan.
   *
   * localStorage kopyası KALDIRILDI. İki kaynak olması gerçek bir arızaya yol
   * açıyordu: "Kaydet" düğmesi kaydı yalnızca yerele yazdığı için listede
   * sunucuda karşılığı olmayan, kimliği bir zaman damgası olan satırlar
   * çıkıyordu. Böyle bir satırdan kanvas açıldığında sorgu ucu `Guid`
   * bekliyor, gövde çözülemiyor ve istek gövdesiz bir 400 ile dönüyordu —
   * ekranda sebebi yazmayan "400 hatası" tam olarak buydu. Aynı şekilde
   * "Sil" de yalnızca yerelden siliyor, kayıt yenilemede geri geliyordu.
   */
  const loadConnections = async () => {
    setIsLoadingConnections(true);
    setLoadError('');
    try {
      const result = await getSavedConnections();

      setConnections((result.connections ?? []).map(conn => ({
        id: conn.id,
        savedConnectionId: conn.id,
        name: conn.name,
        host: conn.host,
        port: conn.port,
        database: conn.database,
        username: conn.username,
        // Şifre tarayıcıda saklanmıyor ve artık hiçbir uç için gerekmiyor;
        // düzenleme formu onu açıldığında ayrıca çözüyor.
        password: '',
        trustServerCertificate: conn.trustServerCertificate,
        createdAt: conn.createdAt,
        updatedAt: conn.updatedAt,
        lastConnectedAt: conn.lastConnectedAt,
        // Seçim sunucuda; tablo/analiz ekranı açılırken oradan okunuyor.
        selectedTables: []
      })));
    } catch (error) {
      console.error('Failed to load connections from database:', error);
      setConnections([]);
      // Sebep ekranda yazıyor. "Tekrar deneyin" tek başına, oturumun mu
      // düştüğünü (401) yetkinin mi yetmediğini (403) sunucunun mu hata
      // verdiğini (5xx) ayırt ettirmiyordu — üçünün de yapılacak şeyi farklı.
      setLoadError(describeLoadFailure(error));
    } finally {
      setIsLoadingConnections(false);
    }
  };

  // Load connections on mount
  React.useEffect(() => {
    loadConnections();
  }, []);

  const handleChange = (e) => {
    const { name, value, type, checked } = e.target;
    setFormData(prev => ({
      ...prev,
      [name]: type === 'checkbox' ? checked : value
    }));
  };

  const isFormIncomplete = () =>
    !formData.name || !formData.host || !formData.database || !formData.username;

  const describeError = (error) =>
    error?.response?.data?.error ?? error?.message ?? 'Bilinmeyen hata';

  /**
   * Formu sunucuya yazar ve bağlantı kimliğini döndürür. Artık TEK kayıt yolu.
   *
   * Önceden iki tane vardı ve ikisi de eksikti: "Kaydet" düğmesi kaydı
   * yalnızca localStorage'a yazıyordu — sunucuda böyle bir bağlantı hiç
   * oluşmuyor, kimliği de bir zaman damgası oluyordu; sunucuya yazan tek yol
   * ise "Test Et"in içine gömülüydü, yani test geçmeden bağlantı
   * kaydedilemiyordu. Bridge'e bağlanacak bir veritabanında test her zaman
   * başarısız olduğu için o bağlantılar hiç kaydedilemiyordu.
   *
   * Sıra artık şu ve tek yönlü: kaydet → bridge'e bağla → test et. Testin
   * bridge'i kullanabilmesi buna bağlı: yol seçimi bağlantının eşleşmesinden,
   * eşleşme de kimliğinden okunuyor.
   */
  const persistConnection = async () => {
    const name = formData.name?.trim() || `${formData.host}-${formData.database}`;
    let connectionId = savedConnectionId;

    if (connectionId) {
      await updateConnection(connectionId, { ...formData, name });
    } else {
      const saved = await saveConnection(name, formData);
      if (!saved?.success || !saved.connectionId) {
        throw new Error(saved?.message ?? 'Bağlantı kaydedilemedi.');
      }
      connectionId = saved.connectionId;
      setSavedConnectionId(connectionId);
    }

    await loadConnections();

    return connectionId;
  };

  const handleTest = async () => {
    if (isFormIncomplete()) {
      setTestStatus({ type: 'error', message: '❌ Lütfen tüm zorunlu alanları doldurun' });
      return;
    }

    setIsTesting(true);
    setTestStatus({ type: '', message: '' });

    try {
      const connectionId = await persistConnection();
      const result = await testConnection(connectionId);

      setTestStatus(result.success
        ? { type: 'success', message: `✅ ${result.message}` }
        : { type: 'error', message: `❌ ${result.message}` });
    } catch (error) {
      console.error('Connection test failed:', error);
      setTestStatus({ type: 'error', message: `❌ ${describeError(error)}` });
    } finally {
      setIsTesting(false);
    }
  };

  const handleSave = async () => {
    if (isFormIncomplete()) {
      setTestStatus({ type: 'error', message: '❌ Lütfen tüm zorunlu alanları doldurun' });
      return;
    }

    try {
      await persistConnection();
      handleCancel();
      setTestStatus({ type: 'success', message: '✅ Bağlantı kaydedildi!' });
    } catch (error) {
      console.error('Connection save failed:', error);
      setTestStatus({ type: 'error', message: `❌ Bağlantı kaydedilemedi: ${describeError(error)}` });
    }
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

  /**
   * Bağlantıyı siler. Silme SUNUCUYA gidiyor: önceden kayıt yalnızca
   * listeden ve localStorage'dan çıkarılıyordu, sunucudaki kayıt duruyordu.
   * Sayfa yenilenince bağlantı geri geliyor, silindiği sanılan veri kaynağı
   * okunmaya devam ediyordu.
   */
  const handleDelete = async (id) => {
    if (!window.confirm('Bu bağlantıyı silmek istediğinizden emin misiniz?')) return;

    try {
      await deleteConnection(id);
      await loadConnections();
    } catch (error) {
      setNotification({
        show: true,
        type: 'error',
        title: 'Bağlantı silinemedi',
        message: describeError(error),
        details: ''
      });
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

  /**
   * Tablo seçimi kutusunu açar.
   *
   * Şifre ÇÖZÜLMÜYOR. Önceden burada `/decrypt` çağrılıp veritabanı parolası
   * tarayıcıya indiriliyordu, çünkü tablo listesi ucu ham kimlik bilgisi
   * istiyordu. Uç artık bağlantı kimliğiyle çalışıyor: parolanın bulutun
   * dışına çıkması için bir sebep kalmadı ve bridge'e bağlı bağlantılarda
   * liste ilk kez geliyor.
   *
   * Seçim sunucudan okunuyor: liste ucu onu taşımıyor, ve varsayılan olarak
   * boş bırakmak "hiç tablo seçilmemiş" gibi görünmesine yol açıyordu.
   */
  const handleOpenModal = async (connection) => {
    const connectionId = connection.savedConnectionId || connection.id;

    setCurrentConnectionId(connectionId);
    setSelectedConnectionForModal({ id: connectionId, name: connection.name });

    let selected = connection.selectedTables ?? [];
    try {
      const stored = await getSelectedTables(connectionId);
      if (Array.isArray(stored?.tables)) {
        selected = stored.tables.map((fullName) => ({ fullName, tableName: fullName }));
      }
    } catch (error) {
      console.error('Kayıtlı tablo seçimi okunamadı:', error);
    }

    setSelectedTablesForSave(selected);
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

  // Seçimin doğru kaynağı sunucu. Buradaki liste kopyası yalnızca açık olan
  // ekranın anlık gösterimi; bir sonraki açılışta seçim yine sunucudan okunuyor.
  const handleSaveSelectedTables = async () => {
    if (!currentConnectionId) return;

    try {
      await saveSelectedTables(
        currentConnectionId,
        selectedTablesForSave.map(t => t.fullName || t.name).filter(Boolean)
      );
    } catch (error) {
      setNotification({
        show: true,
        type: 'error',
        title: 'Tablo seçimi kaydedilemedi',
        message: describeError(error),
        details: ''
      });
      return;
    }

    setConnections(current => current.map(conn =>
      (conn.savedConnectionId || conn.id) === currentConnectionId
        ? { ...conn, selectedTables: selectedTablesForSave, analysisStatus: 'none', updatedAt: new Date().toISOString() }
        : conn
    ));

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
    // Ön analiz uçları da bağlantı kimliğiyle çalışıyor; şifre çözmeye
    // gerek yok. Seçili tablolar sunucudan okunuyor — panel bunları
    // listelediği için boş bırakmak "hiç tablo seçilmemiş" gibi görünüyordu.
    const connectionId = connection.savedConnectionId || connection.id;

    let selectedTables = connection.selectedTables ?? [];
    try {
      const stored = await getSelectedTables(connectionId);
      if (Array.isArray(stored?.tables)) {
        selectedTables = stored.tables.map((fullName) => ({ fullName, tableName: fullName }));
      }
    } catch (error) {
      console.error('Kayıtlı tablo seçimi okunamadı:', error);
    }

    setSelectedConnectionForAnalysis({ ...connection, savedConnectionId: connectionId, selectedTables });
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
      const connectionId = selectedConnectionForAnalysis.savedConnectionId;
      const tables = selectedConnectionForAnalysis.selectedTables?.map(t => t.fullName) || [];

      let result;
      switch (type) {
        case 'quality':
          result = await getDataQuality(connectionId, tables);
          break;
        case 'statistics':
          result = await getStatistics(connectionId, tables);
          break;
        case 'missing':
          result = await getMissingData(connectionId, tables);
          break;
        case 'relationships':
          result = await getRelationships(connectionId, tables);
          break;
        default:
          return;
      }

      setAnalysisResults(result);
    } catch (error) {
      setAnalysisResults({
        success: false,
        message: `Analiz başarısız: ${describeError(error)}`
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

  /**
   * Kurulum yönergesini açar.
   *
   * Panelin burada üreteceği bir şey yok: bridge açılışta kendi kodunu
   * gösteriyor ve onay tarayıcıda veriliyor (device flow). Önceki sürümde
   * burada tek kullanımlık bir token üretiliyor ve kuran kişi onu
   * `appsettings.json`'a elle yapıştırıyordu.
   */
  const handleShowInstallGuide = () => {
    setEnrollment({ open: true });
    // Dosya bilgisi kurulum paneli acilinca soruluyor: sayfa her acildiginda
    // sormak, kimsenin bakmadigi bir ucu her ziyarette calistirmak olurdu.
    if (!installer) loadInstaller();
  };

  const handleRevokeBridge = async (bridge) => {
    const label = bridge.name || bridge.machineName;
    if (!window.confirm(
      `“${label}” bridge’inin erişimi iptal edilsin mi? ` +
      'Bu bridge üzerinden okunan bağlantılar çalışmayı durdurur.'
    )) return;

    try {
      await revokeBridge(bridge.id);
      await loadBridges();
    } catch (error) {
      console.error('Bridge iptal edilemedi:', error);
      setNotification({
        show: true,
        type: 'error',
        title: 'Bridge iptal edilemedi',
        message: '',
        details: error?.response?.data?.error ?? error.message,
      });
    }
  };


  return (
    <div className="sql-connection-settings">
      {/* Baslik, diger ayar sayfalariyla ayni kaliptan: eyebrow + buyuk
          baslik + aciklama. Onceki hali Tabler'in kucuk sayfa basligiydi ve
          menuden gecerken tek basina farkli bir uygulama gibi duruyordu.
          embedded=true iken DataSourcesPage kendi basligini zaten cizdigi
          icin burasi tekrar cizilmez, yalnizca "+ Yeni Baglanti" butonu
          sekme pilinin yanina tasinir (bkz. DataSourcesPage.jsx). */}
      {!embedded && (
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
      )}
      {embedded && (
        <div className="st-head-actions" style={{ marginBottom: 16, justifyContent: 'flex-end', display: 'flex' }}>
          <button className="st-btn" onClick={handleNewConnection}>
            + Yeni Bağlantı
          </button>
        </div>
      )}

      {testStatus.message && (
        <div className={`gf-alert gf-alert--${testStatus.type === 'error' ? 'danger' : testStatus.type}`}>
          {testStatus.message}
        </div>
      )}

      {/* ── Bridge'ler ────────────────────────────────────────────────
          Veritabanı firewall arkasındaysa buluttan doğrudan bağlantı
          kurulamıyor. Bridge yönü çeviriyor: bağlantıyı müşterinin kendi
          sunucusu dışarı doğru kurar, firewall'da hiçbir port açılmaz.

          Bölüm bilerek bağlantı listesinin ÜSTÜNDE: bir bağlantı bridge
          üzerinden okunacaksa bridge'in önce kurulmuş olması gerekiyor. */}
      <div className="bridge-section">
        <div className="bridge-section__head">
          <div>
            <h3>Bridge’ler</h3>
            <p className="st-lead" style={{ marginTop: 4 }}>
              Veritabanınız firewall arkasındaysa, kendi sunucunuza kurduğunuz
              bridge bağlantıyı dışarı doğru kurar. Firewall’da hiçbir port
              açmanız gerekmez.
            </p>
          </div>
          <button className="st-btn" onClick={handleShowInstallGuide}>
            + Bridge Ekle
          </button>
        </div>

        {bridgeError && (
          <div className="gf-alert gf-alert--warning">{bridgeError}</div>
        )}

        {enrollment && (
          <div className="gf-alert gf-alert--info bridge-enrollment">
            <p>
              <strong>Bridge kurulumu.</strong> Kopyalayıp taşıyacağınız bir
              token yok — uygulamayı indirip çalıştırıyor, tek bir düğmeyle
              kendi hesabınızla giriş yapıyorsunuz.
            </p>
            <ol className="bridge-enrollment__steps">
              <li>
                Kurulum dosyasını indirip veritabanına erişebilen makineye
                kopyalayın.
                <div className="bridge-enrollment__download">
                  {installer === null ? (
                    <span className="bridge-enrollment__note">
                      Kurulum dosyası kontrol ediliyor…
                    </span>
                  ) : installer.available ? (
                    <>
                      <a
                        href={bridgeInstallerUrl}
                        className="gf-btn gf-btn--primary gf-btn--sm"
                      >
                        Bridge’i indir (Windows)
                      </a>
                      {installer.sizeBytes && (
                        <span className="bridge-enrollment__note">
                          {(installer.sizeBytes / 1048576).toFixed(0)} MB
                          {installer.publishedAt &&
                            ` · ${new Date(installer.publishedAt).toLocaleDateString('tr-TR')}`}
                        </span>
                      )}
                    </>
                  ) : (
                    <span className="bridge-enrollment__note">
                      Kurulum dosyası bu ortamda yayınlanmamış. Sunucu
                      yöneticinizden <code>BridgeInstaller</code> ayarını
                      yapmasını isteyin.
                    </span>
                  )}
                </div>
              </li>
              <li>
                Uygulamayı çalıştırıp <strong>Giriş Yap</strong>’a basın; tarayıcıda
                Grafirio giriş sayfası açılacak.
              </li>
              <li>
                <strong>Kendi hesabınızla</strong> giriş yapın. Bridge hangi şirkete
                bağlanacağını sizin hesabınızdan öğreniyor; ayrıca bir şey
                seçmenize gerek yok.
              </li>
              <li>Girişten sonra bridge kendini tanıtacak ve aşağıdaki listede görünecek.</li>
            </ol>
            <p className="bridge-enrollment__note">
              Kurulum makinesinde açılacak port yok; bridge bağlantıyı dışarı
              doğru kurar. Onay süresi dolarsa uygulama kendiliğinden yeniden
              dener.
            </p>
            <div className="bridge-enrollment__actions">
              <button
                className="gf-btn gf-btn--sm"
                onClick={async () => { setEnrollment(null); await loadBridges(); }}
              >
                Kapat ve listeyi yenile
              </button>
            </div>
          </div>
        )}

        {bridges.length === 0 ? (
          !bridgeError && (
            <p className="bridge-empty">
              Tanımlı bridge yok. Bağlantılarınız buluttan doğrudan kuruluyor.
            </p>
          )
        ) : (
          <div className="bridge-list">
            {bridges.map((bridge) => (
              <div key={bridge.id} className="bridge-row">
                <span className={`bridge-dot ${bridge.online ? 'is-online' : 'is-offline'}`} />
                <div className="bridge-row__info">
                  <strong>{bridge.name || bridge.machineName}</strong>
                  <small>
                    {bridge.machineName} · sürüm {bridge.version || '—'} ·{' '}
                    {bridge.lastSeenAt
                      ? `son görülme ${new Date(bridge.lastSeenAt).toLocaleString('tr-TR')}`
                      : 'henüz hiç bağlanmadı'}
                  </small>
                </div>
                <span className={`badge ${bridge.online ? 'badge-success' : 'badge-danger'}`}>
                  {bridge.online ? 'Çevrimiçi' : 'Çevrimdışı'}
                </span>
                <button
                  className="gf-btn gf-btn--sm gf-btn--danger"
                  onClick={() => handleRevokeBridge(bridge)}
                >
                  İptal Et
                </button>
              </div>
            ))}
          </div>
        )}
      </div>

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

            {/* Sorguların hangi yoldan gideceği SORULMUYOR: şirketin çevrimiçi
                bir masaüstü uygulaması varsa hepsi oradan okunuyor. Kullanıcı
                seçim yapmıyor, yalnızca durumu görüyor. */}
            <div className="form-group">
              <small className="form-hint">
                {onlineBridge()
                  ? `Sorgular masaüstü uygulamanız (${onlineBridge().name || onlineBridge().machineName}) ` +
                    "üzerinden çalışacak; veritabanı şifreniz sizin makinenizde kalır."
                  : "Sorgular Grafirio sunucudan doğrudan çalışacak. Veritabanınız firewall " +
                    "arkasındaysa masaüstü uygulamasını kurun; kurulduğunda bu bağlantı da " +
                    "otomatik olarak oradan okunur."}
              </small>
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

                  {/* Sorgunun hangi yoldan gittiği. Bağlantı başına bir seçim
                      değil, şirket geneli bir durum: çevrimiçi masaüstü
                      uygulaması varsa hepsi oradan okunuyor. */}
                  {(() => {
                    const bridge = onlineBridge();

                    return bridge ? (
                      <div className="detail-item">
                        <i className="ti ti-transfer"></i>
                        <span>{bridge.name || bridge.machineName || "Masaüstü uygulaması"}</span>
                        <span className="badge badge-success">Çevrimiçi</span>
                      </div>
                    ) : (
                      <div className="detail-item">
                        <i className="ti ti-cloud"></i>
                        <span>Doğrudan bağlantı</span>
                      </div>
                    );
                  })()}
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
                    connectionId={selectedConnectionForModal.id}
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
