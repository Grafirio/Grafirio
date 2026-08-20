import axios from 'axios';
import keycloak from '../keycloak';

const API_BASE_URL = import.meta.env.VITE_API_URL
  ? `${import.meta.env.VITE_API_URL}/data-analysis`
  : 'http://localhost:5000/data-analysis';

/**
 * DataAnalysis.Api artik kimlik dogrulamasi istiyor ve kullanici/firma
 * bilgisini token'dan okuyor; onceden hicbir cagri Authorization gondermiyordu
 * cunku uclar aciktaydi ve kimlik sorgu dizesinden geliyordu. Tek tek 24 cagriya
 * baslik eklemek yerine interceptor: yeni bir cagri yazan kisinin bunu
 * hatirlamasi gerekmiyor.
 */
axios.interceptors.request.use((config) => {
  if (config.url?.startsWith(API_BASE_URL) && keycloak.token) {
    config.headers = config.headers ?? {};
    config.headers.Authorization = `Bearer ${keycloak.token}`;
  }
  return config;
});

/**
 * Host alanına "sunucu,1433" ya da "sunucu:1433" yazmak yaygın bir alışkanlık.
 * Ayrı Port alanıyla birleşince sunucu adresi "sunucu,1433,1433" oluyor ve
 * bağlantı hiçbir zaman kurulamıyordu. Gömülü portu ayıklayıp tek yerde topla.
 */
export const normalizeHostAndPort = (host, port) => {
  const trimmed = String(host ?? '').trim();
  const separator = Math.max(trimmed.lastIndexOf(','), trimmed.lastIndexOf(':'));

  if (separator > 0) {
    const tail = trimmed.slice(separator + 1).trim();
    const bareHost = trimmed.slice(0, separator).trim();
    // IPv6 adreslerinde ':' adresin parçası — yalnızca tek ayraç varsa güvenli.
    if (/^\d+$/.test(tail) && bareHost && !bareHost.includes(':')) {
      return { host: bareHost, port: Number(port) > 0 ? Number(port) : Number(tail) };
    }
  }

  return { host: trimmed, port: Number(port) > 0 ? Number(port) : 1433 };
};

/**
 * Kayıtlı bir bağlantıya ulaşılabiliyor mu.
 *
 * Kimlik bilgisi değil bağlantı KİMLİĞİ gönderiliyor. Sebebi mimari: test,
 * sorgunun gerçekte gideceği yoldan gitmeli — şirketin çevrimiçi bir masaüstü
 * uygulaması varsa oradan, yoksa buluttan. Ham host/kullanıcı/şifre gönderen
 * eski uç bunu yapamıyor, firewall arkasındaki her veritabanı için hep
 * başarısız oluyordu; test geçmeden kayıt da yapılmadığı için o bağlantılar
 * hiç kaydedilemiyordu.
 *
 * Bu yüzden sıra da değişti: önce kaydet, sonra test et.
 */
export const testConnection = async (connectionId) => {
  try {
    const response = await axios.post(
      `${API_BASE_URL}/api/connections/${connectionId}/test`, null, { timeout: 30000 }
    );
    return response.data;
  } catch (error) {
    console.error('Connection test failed:', error);
    throw error;
  }
};

/**
 * Bağlantıyı kaydeder. userId ve companyId ARTIK GÖNDERİLMİYOR: sunucu ikisini
 * de token'dan okuyor ve gövdedekini yok sayıyordu — arayüz ise oraya
 * 'user-123' gibi uydurma değerler koyuyordu.
 */
export const saveConnection = async (name, connectionInfo) => {
  try {
    const { host, port } = normalizeHostAndPort(connectionInfo.host, connectionInfo.port);
    const payload = {
      name,
      host,
      port,
      database: connectionInfo.database,
      username: connectionInfo.username,
      password: connectionInfo.password,
      trustServerCertificate: connectionInfo.trustServerCertificate
    };
    
    const response = await axios.post(`${API_BASE_URL}/api/connections`, payload, {
      timeout: 10000
    });
    return response.data;
  } catch (error) {
    console.error('Save connection failed:', error);    if (error.response) {
      console.error('Response data:', error.response.data);
      console.error('Response status:', error.response.status);
      console.error('Response headers:', error.response.headers);
    }    throw error;
  }
};

/**
 * Firmanin kayitli baglantilari. Eskiden userId sorgu dizesinde gidiyordu;
 * sunucu artik onu yok sayip token'daki firmayi kullaniyor, cunku istemcinin
 * gonderdigi kimlige guvenmek baskasinin baglantilarini okumaya aciktir.
 */
export const getSavedConnections = async () => {
  try {
    const response = await axios.get(`${API_BASE_URL}/api/connections`, {
      timeout: 10000
    });
    console.log('📡 API Response:', response.data);
    console.log('📡 Response keys:', Object.keys(response.data || {}));
    return response.data;
  } catch (error) {
    console.error('Get connections failed:', error);
    throw error;
  }
};

/**
 * Bağlantı, şifresi çözülmüş halde. Tek kullanım yeri düzenleme formu:
 * kullanıcı şifreyi yeniden yazmak zorunda kalmasın diye. Tablo listesi ve
 * ön analiz artık bunu ÇAĞIRMIYOR — o uçlar bağlantı kimliğiyle çalıştığı
 * için parolanın tarayıcıya inmesi gerekmiyor.
 */
export const getConnectionById = async (connectionId) => {
  try {
    const response = await axios.get(`${API_BASE_URL}/api/connections/${connectionId}/decrypt`, {
      timeout: 10000
    });
    return response.data;
  } catch (error) {
    console.error('Get connection failed:', error);
    throw error;
  }
};

/**
 * Bağlantının şifresiz özeti (ad, host, veritabanı).
 *
 * Kanvas gibi yalnızca adı gösteren yerler bunu kullanmalı. Önceden oralar da
 * `getConnectionById` çağırıyordu; o uç şifre çözdüğü için DATA_SOURCES.UPDATE
 * yetkisi istiyor — yani "analizleri görsün ama veri kaynağını değiştirmesin"
 * denen bir kullanıcıda kanvas, sebebi görünmeyen bir yetki hatasıyla boş
 * açılıyordu.
 */
export const getConnectionSummary = async (connectionId) => {
  try {
    const response = await axios.get(
      `${API_BASE_URL}/api/connections/${connectionId}`, { timeout: 10000 }
    );
    return response.data;
  } catch (error) {
    console.error('Get connection summary failed:', error);
    throw error;
  }
};

/**
 * Kayıtlı bağlantıyı günceller. Boş bırakılan alanlar sunucuda olduğu gibi
 * kalır; şifre alanı boşsa mevcut şifre korunur.
 */
export const updateConnection = async (connectionId, changes) => {
  try {
    const { host, port } = normalizeHostAndPort(changes.host, changes.port);
    const response = await axios.put(
      `${API_BASE_URL}/api/connections/${connectionId}`,
      { ...changes, host, port },
      { timeout: 10000 }
    );
    return response.data;
  } catch (error) {
    console.error('Update connection failed:', error);
    throw error;
  }
};

/**
 * Bağlantıyı siler.
 *
 * Bu çağrı EKSİKTİ: "Sil" düğmesi kaydı yalnızca localStorage'dan çıkarıyordu.
 * Sunucudaki kayıt duruyor, sayfa yenilenince bağlantı geri geliyordu — ve
 * silindiği sanılan bir veri kaynağı okunmaya devam ediyordu.
 */
export const deleteConnection = async (connectionId) => {
  try {
    const response = await axios.delete(
      `${API_BASE_URL}/api/connections/${connectionId}`, { timeout: 10000 }
    );
    return response.data;
  } catch (error) {
    console.error('Delete connection failed:', error);
    throw error;
  }
};

/**
 * Bağlantıdaki tablolar. Kimlik bilgisi değil bağlantı kimliği gönderiliyor;
 * sebebi `testConnection` ile aynı — eski uç bridge'i atlıyor ve tablo listesi
 * firewall arkasındaki veritabanlarında hiç gelmiyordu. Tablo seçilemeyince
 * "Analiz Et" de başlamıyor, sorgu da hiç çalıştırılamıyordu.
 *
 * Yan etkisi: veritabanı parolasının artık tarayıcıya inmesi gerekmiyor.
 */
export const getTables = async (connectionId) => {
  try {
    const response = await axios.get(
      `${API_BASE_URL}/api/schema/${connectionId}/tables`, { timeout: 30000 }
    );
    return response.data;
  } catch (error) {
    console.error('Failed to get tables:', error);
    throw error;
  }
};

/* ─────────────────────────────────────────────────────────────
   Ön analiz uçları

   Yol ONARILDI: adres `/analysis/...` yazılıyordu, `/api` öneki eksikti.
   Yani bu dört düğme hiçbir zaman çalışmamış, hep 404 almıştı — hata
   "Analiz başarısız" diye gösterildiği için uç bulunamadığı anlaşılmıyordu.
───────────────────────────────────────────────────────────── */
const runPreAnalysis = async (connectionId, kind, tables) => {
  try {
    const response = await axios.post(
      `${API_BASE_URL}/api/analysis/${connectionId}/${kind}`, { tables }, { timeout: 60000 }
    );
    return response.data;
  } catch (error) {
    console.error(`Pre-analysis '${kind}' failed:`, error);
    throw error;
  }
};

export const getDataQuality = (connectionId, tables) =>
  runPreAnalysis(connectionId, 'data-quality', tables);

export const getStatistics = (connectionId, tables) =>
  runPreAnalysis(connectionId, 'statistics', tables);

export const getMissingData = (connectionId, tables) =>
  runPreAnalysis(connectionId, 'missing-data', tables);

export const getRelationships = (connectionId, tables) =>
  runPreAnalysis(connectionId, 'relationships', tables);

/* ─────────────────────────────────────────────────────────────
   Analiz hattı

   Tek adım: "Analiz Et". Seçili tabloların profilini çıkarır, örnek
   değerlere bakarak semantik sözlük üretir ve çözemediği kolonları
   kullanıcıya sorar. Sözlük hem analizin çıktısı hem de sorgu anında
   modelin gördüğü tek kaynak.

   Önceden bu iş "Ön Analiz" ve "Analiz Et" diye ikiye bölünmüştü;
   kullanıcı ikisini de doğru sırayla çalıştırmak zorundaydı ve sorgu
   yalnızca ikincisinin çıktısını okuyordu.
───────────────────────────────────────────────────────────── */

/**
 * Analizi başlatır. İş arka planda yürüdüğü için uç hemen 202 döner;
 * ilerleme `getAnalysisStatus` ile takip edilir.
 */
export const startAnalysis = async (connectionId, samplingConsentGiven = false) => {
  try {
    const response = await axios.post(
      `${API_BASE_URL}/api/agent/analyze-connection/${connectionId}`,
      { samplingConsentGiven },
      { timeout: 30000 }
    );
    return response.data;
  } catch (error) {
    console.error('Analysis start failed:', error);
    throw error;
  }
};

/** Analiz durumu + varsa kullanıcıya sorulacak sorular. */
export const getAnalysisStatus = async (connectionId) => {
  try {
    const response = await axios.get(`${API_BASE_URL}/api/agent/config/${connectionId}/status`, {
      timeout: 10000
    });
    return response.data;
  } catch (error) {
    console.error('Analysis status check failed:', error);
    throw error;
  }
};

/** Soru yanıtlarını sözlüğe işler; bağlantı `ready` olur. */
export const submitAnalysisAnswers = async (connectionId, answers) => {
  const response = await axios.post(
    `${API_BASE_URL}/api/agent/config/${connectionId}/answers`,
    { answers },
    { timeout: 30000 }
  );
  return response.data;
};

/** Firmanın analiz edilmiş bağlantıları — panel bunu listeler. */
export const listAnalyses = async () => {
  const response = await axios.get(`${API_BASE_URL}/api/agent/configs`, { timeout: 15000 });
  return response.data;
};


// Doğal dil sorgusu gönder → LLM + PyCaret
export const submitAgentQuery = async (connectionId, question) => {
  try {
    const response = await axios.post(`${API_BASE_URL}/api/agent/query`, {
      connectionId,
      question
    }, {
      timeout: 120000 // 2 dakika
    });
    return response.data;
  } catch (error) {
    console.error('Agent query failed:', error);
    throw error;
  }
};

// Sorgu durumunu kontrol et
export const getAgentQueryStatus = async (queryId) => {
  try {
    const response = await axios.get(`${API_BASE_URL}/api/agent/query/${queryId}/status`, {
      timeout: 10000
    });
    return response.data;
  } catch (error) {
    console.error('Query status check failed:', error);
    throw error;
  }
};

// Sorgu sonucunu getir
export const getAgentQueryResult = async (queryId) => {
  try {
    const response = await axios.get(`${API_BASE_URL}/api/agent/query/${queryId}/result`, {
      timeout: 10000
    });
    return response.data;
  } catch (error) {
    console.error('Query result fetch failed:', error);
    throw error;
  }
};

// Sorgu geçmişini getir
export const getAgentQueryHistory = async (connectionId) => {
  try {
    const response = await axios.get(`${API_BASE_URL}/api/agent/queries/${connectionId}`, {
      timeout: 10000
    });
    return response.data;
  } catch (error) {
    console.error('Query history fetch failed:', error);
    throw error;
  }
};

/* ─────────────────────────────────────────────────────────────
   Tablo seçimi

   Seçim eskiden yalnızca localStorage'da tutuluyordu; sunucu hangi
   tabloların seçildiğini bilmediği için "yalnızca seçili tablolar işlenir"
   kuralı uygulanamıyor, şema çıkarma tüm veritabanını tarıyordu.
───────────────────────────────────────────────────────────── */

/** Seçili tabloları sunucuya kaydeder. Seçim değişince analiz geçersiz olur. */
export const saveSelectedTables = async (connectionId, tables) => {
  const response = await axios.put(
    `${API_BASE_URL}/api/connections/${connectionId}/tables`,
    { tables },
    { timeout: 20000 }
  );
  return response.data;
};

export const getSelectedTables = async (connectionId) => {
  const response = await axios.get(
    `${API_BASE_URL}/api/connections/${connectionId}/tables`,
    { timeout: 20000 }
  );
  return response.data;
};

/* ─────────────────────────────────────────────────────────────
   Bridge — müşterinin kendi ağında çalışan bağlantı servisi.

   Neden var: kurumsal veritabanları firewall arkasında ve buluttan
   erişilemiyor. Bridge yönü çeviriyor — bağlantıyı müşterinin sunucusu
   dışarı doğru kurar, firewall'da hiçbir port açılmaz.

   Panelin bu uçlarla işi kurulumu başlatmak ve durumu göstermekle sınırlı.
   Hangi bağlantının hangi makineden okunacağı SORULMUYOR: şirketin
   çevrimiçi bir bridge'i varsa hepsi oradan okunuyor. Eşleştirme uçları
   bu yüzden kaldırıldı — kullanıcıya sorulacak bir soru değildi ve
   yapılmadığında kurulum sessizce işe yaramıyordu.

   Kurulumun kendisi panelden geçmiyor — bridge açılışta kendi kodunu
   gösteriyor ve onay Keycloak'ın device flow ekranında veriliyor.
───────────────────────────────────────────────────────────── */

/** Şirketin bridge'leri ve çevrimiçi durumları. */
export const getBridges = async () => {
  const response = await axios.get(`${API_BASE_URL}/api/bridges`, { timeout: 15000 });
  return response.data;
};

/** Kurulum dosyası bu ortamda yayınlanmış mı. Uç anonim: dosya gizli değil. */
export const getBridgeInstallerInfo = async () => {
  const response = await axios.get(
    `${API_BASE_URL}/api/bridges/installer/info`, { timeout: 15000 }
  );
  return response.data;
};

/**
 * İndirme bağlantısı.
 *
 * Uç ANONİM — kurulum dosyasını şirkete bağlayan şey dosya değil, giriş
 * ekranındaki onay; dosyanın kendisi zaten public bir blob'a yönlendiriliyor.
 * Bu yüzden düz bir <a href> yetiyor; Authorization başlığı taşımak ya da
 * dosyayı blob olarak alıp geçici bir bağlantıyla indirtmek gerekmiyor.
 */
export const bridgeInstallerUrl = `${API_BASE_URL}/api/bridges/installer`;

/** Bridge'in erişimini iptal eder. */
export const revokeBridge = async (bridgeId) => {
  const response = await axios.delete(
    `${API_BASE_URL}/api/bridges/${bridgeId}`, { timeout: 15000 }
  );
  return response.data;
};

