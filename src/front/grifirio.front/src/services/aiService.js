import axios from 'axios';

const SCHEMA_ANALYZER_URL = import.meta.env.VITE_SCHEMA_ANALYZER_URL || 'http://localhost:8001';
const PYCARET_ENGINE_URL = import.meta.env.VITE_PYCARET_ENGINE_URL || 'http://localhost:8002';
const DJANGO_AI_URL = import.meta.env.VITE_DJANGO_AI_URL || 'http://localhost:8000';

// Schema Analyzer API
export const schemaAnalyzerAPI = {
  /**
   * Analyze database schema
   * @param {Object} connectionInfo - Database connection info
   * @returns {Promise} Schema analysis result
   */
  analyzeSchema: async (connectionInfo) => {
    try {
      const response = await axios.post(`${SCHEMA_ANALYZER_URL}/analyze`, connectionInfo);
      return response.data;
    } catch (error) {
      console.error('Schema analysis error:', error);
      throw error;
    }
  },

  /**
   * Test database connection
   * @param {Object} connectionInfo - Database connection info
   * @returns {Promise} Connection test result
   */
  testConnection: async (connectionInfo) => {
    try {
      const response = await axios.post(`${SCHEMA_ANALYZER_URL}/test-connection`, connectionInfo);
      return response.data;
    } catch (error) {
      console.error('Connection test error:', error);
      throw error;
    }
  },

  /**
   * Health check
   * @returns {Promise} Service status
   */
  healthCheck: async () => {
    try {
      const response = await axios.get(`${SCHEMA_ANALYZER_URL}/`);
      return response.data;
    } catch (error) {
      console.error('Health check error:', error);
      throw error;
    }
  }
};

// PyCaret Engine API
export const pycaretEngineAPI = {
  /**
   * Train a new model
   * @param {Object} trainingData - Training configuration
   * @returns {Promise} Training job ID
   */
  trainModel: async (trainingData) => {
    try {
      const response = await axios.post(`${PYCARET_ENGINE_URL}/train`, trainingData);
      return response.data;
    } catch (error) {
      console.error('Model training error:', error);
      throw error;
    }
  },

  /**
   * Get training status
   * @param {string} jobId - Training job ID
   * @returns {Promise} Training status
   */
  getTrainingStatus: async (jobId) => {
    try {
      const response = await axios.get(`${PYCARET_ENGINE_URL}/train/status/${jobId}`);
      return response.data;
    } catch (error) {
      console.error('Get training status error:', error);
      throw error;
    }
  },

  /**
   * Make prediction
   * @param {Object} predictionData - Prediction request
   * @returns {Promise} Prediction result
   */
  predict: async (predictionData) => {
    try {
      const response = await axios.post(`${PYCARET_ENGINE_URL}/predict`, predictionData);
      return response.data;
    } catch (error) {
      console.error('Prediction error:', error);
      throw error;
    }
  },

  /**
   * Health check
   * @returns {Promise} Service status
   */
  healthCheck: async () => {
    try {
      const response = await axios.get(`${PYCARET_ENGINE_URL}/`);
      return response.data;
    } catch (error) {
      console.error('Health check error:', error);
      throw error;
    }
  }
};

// Django AI Service API
export const djangoAIAPI = {
  /**
   * Register a new company for AI monitoring
   * @param {Object} companyData - Company information
   * @returns {Promise} Registration result
   */
  registerCompany: async (companyData) => {
    try {
      const response = await axios.post(`${DJANGO_AI_URL}/api/companies/register`, companyData);
      return response.data;
    } catch (error) {
      console.error('Company registration error:', error);
      throw error;
    }
  },

  /**
   * Get company AI insights
   * @param {string} companyId - Company ID
   * @returns {Promise} AI insights
   */
  getCompanyInsights: async (companyId) => {
    try {
      const response = await axios.get(`${DJANGO_AI_URL}/api/companies/${companyId}/insights`);
      return response.data;
    } catch (error) {
      console.error('Get insights error:', error);
      throw error;
    }
  },

  /**
   * Trigger manual data sync
   * @param {string} companyId - Company ID
   * @returns {Promise} Sync result
   */
  triggerSync: async (companyId) => {
    try {
      const response = await axios.post(`${DJANGO_AI_URL}/api/companies/${companyId}/sync`);
      return response.data;
    } catch (error) {
      console.error('Trigger sync error:', error);
      throw error;
    }
  }
};

export default {
  schemaAnalyzerAPI,
  pycaretEngineAPI,
  djangoAIAPI
};
