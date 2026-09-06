export const DATA_ANALYSIS_API_URL = import.meta.env?.VITE_API_URL
  ? `${import.meta.env.VITE_API_URL}/data-analysis`
  : 'http://localhost:5000/data-analysis';