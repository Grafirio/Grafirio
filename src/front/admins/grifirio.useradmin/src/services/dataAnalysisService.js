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

// Test SQL Server connection
export const testConnection = async (connectionInfo) => {
  try {
    const { host, port } = normalizeHostAndPort(connectionInfo.host, connectionInfo.port);
    const response = await axios.post(`${API_BASE_URL}/api/connections/test`, {
      ...connectionInfo,
      host,
      port
    }, {
      timeout: 15000
    });
    return response.data;
  } catch (error) {
    console.error('Connection test failed:', error);
    throw error;
  }
};

// Save SQL Server connection
export const saveConnection = async (userId, companyId, name, connectionInfo) => {
  try {
    const { host, port } = normalizeHostAndPort(connectionInfo.host, connectionInfo.port);
    const payload = {
      userId,
      companyId,
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

// Get connection by ID (with decrypted password)
export const getConnectionById = async (connectionId) => {
  try {
    // Decrypt endpoint'ini kullan - şifreyi çözülmüş olarak getir
    const response = await axios.get(`${API_BASE_URL}/api/connections/${connectionId}/decrypt`, {
      timeout: 10000
    });
    return response.data;
  } catch (error) {
    console.error('Get connection failed:', error);
    throw error;
  }
};

// Get list of tables from database
export const getTables = async (connectionInfo) => {
  try {
    const response = await axios.post(`${API_BASE_URL}/api/schema/tables`, connectionInfo, {
      timeout: 30000
    });
    return response.data;
  } catch (error) {
    console.error('Failed to get tables:', error);
    throw error;
  }
};

// Get detailed schema for a specific table
export const getTableSchema = async (tableName, connectionInfo) => {
  try {
    const response = await axios.post(
      `${API_BASE_URL}/api/schema/table/${encodeURIComponent(tableName)}`, 
      connectionInfo,
      { timeout: 30000 }
    );
    return response.data;
  } catch (error) {
    console.error('Failed to get table schema:', error);
    throw error;
  }
};

// Check Data Analysis API health
export const checkApiHealth = async () => {
  try {
    const response = await axios.get(`${API_BASE_URL.replace('/api', '')}/health`, {
      timeout: 5000
    });
    return response.data;
  } catch (error) {
    console.error('API health check failed:', error);
    return { status: 'Unhealthy' };
  }
};

// Pre-Analysis Functions
export const getDataQuality = async (connectionInfo, tables) => {
  try {
    const response = await axios.post(`${API_BASE_URL}/analysis/data-quality`, {
      connectionInfo,
      tables
    }, {
      timeout: 60000
    });
    return response.data;
  } catch (error) {
    console.error('Data quality analysis failed:', error);
    throw error;
  }
};

export const getStatistics = async (connectionInfo, tables) => {
  try {
    const response = await axios.post(`${API_BASE_URL}/analysis/statistics`, {
      connectionInfo,
      tables
    }, {
      timeout: 60000
    });
    return response.data;
  } catch (error) {
    console.error('Statistics analysis failed:', error);
    throw error;
  }
};

export const getMissingData = async (connectionInfo, tables) => {
  try {
    const response = await axios.post(`${API_BASE_URL}/analysis/missing-data`, {
      connectionInfo,
      tables
    }, {
      timeout: 60000
    });
    return response.data;
  } catch (error) {
    console.error('Missing data analysis failed:', error);
    throw error;
  }
};

export const getRelationships = async (connectionInfo, tables) => {
  try {
    const response = await axios.post(`${API_BASE_URL}/analysis/relationships`, {
      connectionInfo,
      tables
    }, {
      timeout: 60000
    });
    return response.data;
  } catch (error) {
    console.error('Relationship analysis failed:', error);
    throw error;
  }
};

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

// Semantik sözlüğü getir
export const getAgentConfig = async (connectionId) => {
  try {
    const response = await axios.get(`${API_BASE_URL}/api/agent/config/${connectionId}`, {
      timeout: 10000
    });
    return response.data;
  } catch (error) {
    console.error('Get config failed:', error);
    throw error;
  }
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
