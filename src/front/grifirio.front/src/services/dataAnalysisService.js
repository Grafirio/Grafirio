import axios from 'axios';

const API_BASE_URL = import.meta.env.VITE_API_URL 
  ? `${import.meta.env.VITE_API_URL}/data-analysis` 
  : 'http://localhost:5000/data-analysis';

// Test SQL Server connection
export const testConnection = async (connectionInfo) => {
  try {
    const response = await axios.post(`${API_BASE_URL}/api/connection/test`, connectionInfo, {
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
    const payload = {
      userId,
      companyId,
      name,
      host: connectionInfo.host,
      port: connectionInfo.port,
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

// Get saved connections for user
export const getSavedConnections = async (userId) => {
  try {
    console.log('🌐 API Call: GET /api/connections?userId=' + userId);
    const response = await axios.get(`${API_BASE_URL}/api/connections`, {
      params: { userId },
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

// Send data to Schema Analyzer AI
export const analyzeSchema = async (schemaData) => {
  try {
    const response = await axios.post('http://localhost:8001/analyze', schemaData, {
      timeout: 60000
    });
    return response.data;
  } catch (error) {
    console.error('Schema analysis failed:', error);
    throw error;
  }
};

// Send data to PyCaret Engine
export const trainModel = async (trainingData) => {
  try {
    const response = await axios.post('http://localhost:8002/train', trainingData, {
      timeout: 120000 // 2 minutes for training
    });
    return response.data;
  } catch (error) {
    console.error('Model training failed:', error);
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

// AI Analysis - MassTransit ile asenkron analiz başlatma
export const startAIAnalysis = async (userId, companyId, connectionId, tables, settings) => {
  try {
    console.log('Starting AI analysis...', { userId, companyId, connectionId, tables, settings });
    
    // API'nin beklediği format (camelCase)
    const payload = {
      userId: userId,
      companyId: companyId,
      connectionId: connectionId,
      tables: tables,
      settings: {
        samplingRate: settings.samplingRate,
        nullHandling: settings.nullHandling,
        dataFormat: settings.dataFormat
      }
    };
    
    console.log('Payload:', JSON.stringify(payload, null, 2));
    
    const response = await axios.post(`${API_BASE_URL}/api/ai/start-analysis`, payload, { 
      timeout: 60000,
      headers: {
        'Content-Type': 'application/json'
      }
    });
    return response.data;
  } catch (error) {
    console.error('AI analysis error:', error);
    if (error.response) {
      console.error('Response data:', error.response.data);
      console.error('Response status:', error.response.status);
      throw new Error(error.response.data?.message || JSON.stringify(error.response.data));
    }
    throw error;
  }
};

// AI Analiz durumunu kontrol etme
export const getAnalysisStatus = async (requestId) => {
  try {
    const response = await axios.get(`${API_BASE_URL}/api/ai/analysis-status/${requestId}`, { timeout: 10000 });
    return response.data;
  } catch (error) {
    console.error('Analysis status check error:', error);
    throw error;
  }
};

// AI Rapor oluşturma
export const generateAIReport = async (requestId, reportType, database, tables) => {
  try {
    console.log('Generating AI report:', { requestId, reportType, database, tables });
    const response = await axios.post(`${API_BASE_URL}/api/ai/reports/generate`, {
      requestId,
      reportType,
      database,
      tables
    }, { timeout: 30000 });
    return response.data;
  } catch (error) {
    console.error('Generate report error:', error);
    throw error;
  }
};

// AI'ya soru sorma
export const askAIQuestion = async (requestId, question, database, tables, options = {}) => {
  try {
    const tableName = options.tableName || null;
    const predictData = options.predictData || null;

    console.log('Asking AI question:', { requestId, question, database, tables, tableName, predictData });
    const response = await axios.post(`${API_BASE_URL}/api/ai/reports/ask-question`, {
      requestId,
      question,
      database,
      tables,
      tableName,
      predictData
    }, { timeout: 30000 });
    return response.data;
  } catch (error) {
    console.error('Ask question error:', error);
    throw error;
  }
};

// ========== AI Agent Pipeline ==========

// Bağlantı schema'sını Gemini ile analiz et → PyCaret config oluştur
export const analyzeConnectionSchema = async (connectionId) => {
  try {
    const response = await axios.post(`${API_BASE_URL}/api/agent/analyze-connection/${connectionId}`, null, {
      timeout: 120000 // 2 dakika — Gemini analizi zaman alabilir
    });
    return response.data;
  } catch (error) {
    console.error('Schema analysis failed:', error);
    throw error;
  }
};

// PyCaret config durumunu kontrol et
export const getAgentConfigStatus = async (connectionId) => {
  try {
    const response = await axios.get(`${API_BASE_URL}/api/agent/config/${connectionId}/status`, {
      timeout: 10000
    });
    return response.data;
  } catch (error) {
    console.error('Config status check failed:', error);
    throw error;
  }
};

// PyCaret config'ini getir
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

// Doğal dil sorgusu gönder → Gemini + PyCaret
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
