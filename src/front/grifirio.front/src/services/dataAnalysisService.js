import axios from 'axios';

const API_BASE_URL = 'http://localhost:5221/api';

// Test SQL Server connection
export const testConnection = async (connectionInfo) => {
  try {
    const response = await axios.post(`${API_BASE_URL}/connection/test`, connectionInfo, {
      timeout: 15000
    });
    return response.data;
  } catch (error) {
    console.error('Connection test failed:', error);
    throw error;
  }
};

// Get list of tables from database
export const getTables = async (connectionInfo) => {
  try {
    const response = await axios.post(`${API_BASE_URL}/schema/tables`, connectionInfo, {
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
      `${API_BASE_URL}/schema/table/${encodeURIComponent(tableName)}`, 
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
export const startAIAnalysis = async (userId, companyId, connectionInfo, tables, settings) => {
  try {
    console.log('Starting AI analysis...', { userId, companyId, tables, settings });
    const response = await axios.post(`${API_BASE_URL}/ai/start-analysis`, {
      userId,
      companyId,
      connectionInfo,
      tables,
      settings
    }, { timeout: 60000 });
    return response.data;
  } catch (error) {
    console.error('AI analysis error:', error);
    throw error;
  }
};

// AI Analiz durumunu kontrol etme
export const getAnalysisStatus = async (requestId) => {
  try {
    const response = await axios.get(`${API_BASE_URL}/ai/analysis-status/${requestId}`, { timeout: 10000 });
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
    const response = await axios.post(`${API_BASE_URL}/ai/reports/generate`, {
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
export const askAIQuestion = async (requestId, question, database, tables) => {
  try {
    console.log('Asking AI question:', { requestId, question, database, tables });
    const response = await axios.post(`${API_BASE_URL}/ai/reports/ask-question`, {
      requestId,
      question,
      database,
      tables
    }, { timeout: 30000 });
    return response.data;
  } catch (error) {
    console.error('Ask question error:', error);
    throw error;
  }
};
