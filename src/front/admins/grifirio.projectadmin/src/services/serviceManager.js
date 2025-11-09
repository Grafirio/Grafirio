import axios from 'axios';

// Tüm servislerin tanımları
export const SERVICES = {
  // AI Services
  schemaAnalyzer: {
    id: 'schema-analyzer',
    name: 'Schema Analyzer',
    category: 'AI Services',
    url: 'http://localhost:8001',
    healthEndpoint: '/',
    type: 'docker',
    containerName: 'schema.analyzer.container',
    port: 8001,
    description: 'Database schema analysis with OpenAI GPT-4o-mini'
  },
  pycaretEngine: {
    id: 'pycaret-engine',
    name: 'PyCaret Engine',
    category: 'AI Services',
    url: 'http://localhost:8002',
    healthEndpoint: '/',
    type: 'docker',
    containerName: 'pycaret.engine.container',
    port: 8002,
    description: 'AutoML training and prediction service'
  },
  djangoAI: {
    id: 'django-ai',
    name: 'Django AI Service',
    category: 'AI Services',
    url: 'http://localhost:8000',
    healthEndpoint: '/api/health/',
    type: 'docker',
    containerName: 'django.ai.container',
    port: 8000,
    description: 'CDC polling, WebSocket server, RabbitMQ consumer'
  },
  celeryWorker: {
    id: 'celery-worker',
    name: 'Celery Worker',
    category: 'AI Services',
    url: null,
    healthEndpoint: null,
    type: 'docker',
    containerName: 'celery.worker.container',
    port: null,
    description: 'Background task processor'
  },

  // .NET Microservices
  identityAPI: {
    id: 'identity-api',
    name: 'Identity API',
    category: '.NET Microservices',
    url: 'http://localhost:5036',
    healthEndpoint: '/swagger',
    type: 'dotnet',
    projectPath: 'src/services/Grafirio.Identity.Api/Grafirio.Identity.Api',
    port: 5036,
    description: 'Authentication and authorization service'
  },
  catalogAPI: {
    id: 'catalog-api',
    name: 'Catalog API',
    category: '.NET Microservices',
    url: 'http://localhost:5280',
    healthEndpoint: '/swagger',
    type: 'dotnet',
    projectPath: 'src/services/Grafirio.Catalog.Api',
    port: 5280,
    description: 'Product catalog management'
  },
  basketAPI: {
    id: 'basket-api',
    name: 'Basket API',
    category: '.NET Microservices',
    url: 'http://localhost:5023',
    healthEndpoint: '/swagger',
    type: 'dotnet',
    projectPath: 'src/services/Grafirio.Basket.Api',
    port: 5023,
    description: 'Shopping basket service'
  },
  dataAnalysisAPI: {
    id: 'data-analysis-api',
    name: 'Data Analysis API',
    category: '.NET Microservices',
    url: 'http://localhost:5221',
    healthEndpoint: '/swagger',
    type: 'dotnet',
    projectPath: 'src/services/Grafirio.DataAnalysis.Api',
    port: 5221,
    description: 'SQL connection & schema discovery for AI analysis'
  },

  // Infrastructure
  rabbitmq: {
    id: 'rabbitmq',
    name: 'RabbitMQ',
    category: 'Infrastructure',
    url: 'http://localhost:15672',
    healthEndpoint: null,
    type: 'docker',
    containerName: 'rabbitmq.container',
    port: 15672,
    managementUI: 'http://localhost:15672',
    credentials: { username: 'guest', password: 'guest123' },
    description: 'Message broker'
  },
  redis: {
    id: 'redis',
    name: 'Redis',
    category: 'Infrastructure',
    url: 'redis://localhost:6379',
    healthEndpoint: null,
    type: 'docker',
    containerName: 'redis.db.container',
    port: 6379,
    description: 'Cache and channel layer'
  },
  mongodbCatalog: {
    id: 'mongodb-catalog',
    name: 'MongoDB Catalog',
    category: 'Infrastructure',
    url: 'mongodb://localhost:27030',
    healthEndpoint: null,
    type: 'docker',
    containerName: 'mongo.db.catalog.container',
    port: 27030,
    credentials: { username: 'myuser', password: 'Password12' },
    description: 'Catalog database'
  },
  mongodbDiscount: {
    id: 'mongodb-discount',
    name: 'MongoDB Discount',
    category: 'Infrastructure',
    url: 'mongodb://localhost:27034',
    healthEndpoint: null,
    type: 'docker',
    containerName: 'mongo.db.discount.container',
    port: 27034,
    credentials: { username: 'myuser', password: 'Password12' },
    description: 'Discount database'
  },
  sqlserver: {
    id: 'sqlserver',
    name: 'SQL Server',
    category: 'Infrastructure',
    url: 'localhost:1433',
    healthEndpoint: null,
    type: 'docker',
    containerName: 'sqlserver.db.order',
    port: 1433,
    credentials: { username: 'sa', password: 'Password12*' },
    description: 'Order database'
  },
  keycloak: {
    id: 'keycloak',
    name: 'Keycloak',
    category: 'Infrastructure',
    url: 'http://localhost:8080',
    healthEndpoint: null,
    type: 'docker',
    containerName: 'keycloak',
    port: 8080,
    managementUI: 'http://localhost:8080',
    credentials: { username: 'admin', password: 'password' },
    description: 'Authentication server'
  },
  postgresql: {
    id: 'postgresql',
    name: 'PostgreSQL',
    category: 'Infrastructure',
    url: 'postgresql://localhost:5432',
    healthEndpoint: null,
    type: 'docker',
    containerName: 'postgres.db.keycloak.container',
    port: 5432,
    credentials: { username: 'keycloak_db_user', password: 'password' },
    description: 'Keycloak database'
  },

  // Frontend
  frontend: {
    id: 'frontend',
    name: 'React Frontend',
    category: 'Frontend',
    url: 'http://localhost:59264',
    healthEndpoint: '/',
    type: 'npm',
    projectPath: 'src/front/grifirio.front',
    port: 59264,
    description: 'Main user interface'
  }
};

// Servis durumunu kontrol et
export const checkServiceHealth = async (service) => {
  try {
    // .NET servisleri için port kontrolü
    if (service.type === 'dotnet' && service.port) {
      try {
        const response = await axios.get(`http://localhost:3001/api/dotnet/status/${service.port}`, { timeout: 3000 });
        if (response.data.running) {
          return { status: 'online', message: 'Service is running on port ' + service.port };
        } else {
          return { status: 'offline', message: 'Service is not running' };
        }
      } catch (err) {
        // API yoksa HTTP health check dene
        if (service.url && service.healthEndpoint) {
          try {
            const healthResponse = await axios.get(service.url + service.healthEndpoint, { timeout: 3000 });
            if (healthResponse.status >= 200 && healthResponse.status < 300) {
              return { status: 'online', message: 'Service is healthy' };
            }
          } catch (healthErr) {
            return { status: 'offline', message: 'Cannot connect' };
          }
        }
        return { status: 'offline', message: 'Service not running' };
      }
    }

    // Docker container için özel kontrol
    if (service.type === 'docker' && service.containerName) {
      try {
        const response = await axios.get(`http://localhost:3001/api/docker/status/${service.containerName}`, { timeout: 3000 });
        if (response.data.running) {
          return { status: 'online', message: 'Container is running' };
        } else {
          return { status: 'offline', message: 'Container is stopped' };
        }
      } catch (err) {
        // API yoksa HTTP health check dene
        if (service.url && service.healthEndpoint) {
          try {
            const healthResponse = await axios.get(service.url + service.healthEndpoint, { timeout: 3000 });
            if (healthResponse.status >= 200 && healthResponse.status < 300) {
              return { status: 'online', message: 'Service is healthy' };
            }
          } catch (healthErr) {
            return { status: 'offline', message: 'Cannot connect' };
          }
        }
        return { status: 'unknown', message: 'Status check unavailable' };
      }
    }

    // NPM/Frontend servisleri için HTTP health check
    if (service.type === 'npm' || (service.url && service.healthEndpoint)) {
      if (!service.url) {
        return { status: 'unknown', message: 'No URL configured' };
      }
      
      try {
        const url = service.healthEndpoint ? service.url + service.healthEndpoint : service.url;
        const response = await axios.get(url, { timeout: 3000 });
        
        if (response.status >= 200 && response.status < 300) {
          return { status: 'online', message: 'Service is healthy' };
        }
        return { status: 'error', message: `HTTP ${response.status}` };
      } catch (error) {
        if (error.code === 'ECONNABORTED') {
          return { status: 'timeout', message: 'Request timeout' };
        }
        return { status: 'offline', message: 'Cannot connect to service' };
      }
    }

    // HTTP health check
    if (!service.healthEndpoint || !service.url) {
      return { status: 'unknown', message: 'No health endpoint configured' };
    }

    const url = service.url + service.healthEndpoint;
    const response = await axios.get(url, { timeout: 3000 });
    
    if (response.status >= 200 && response.status < 300) {
      return { status: 'online', message: 'Service is healthy' };
    }
    return { status: 'error', message: `HTTP ${response.status}` };
  } catch (error) {
    if (error.code === 'ECONNABORTED') {
      return { status: 'timeout', message: 'Request timeout' };
    }
    if (error.code === 'ERR_NETWORK' || error.message.includes('Network Error')) {
      return { status: 'offline', message: 'Cannot connect to service' };
    }
    return { status: 'error', message: error.message };
  }
};

// Tüm servisleri kontrol et
export const checkAllServices = async () => {
  const results = {};
  
  for (const [key, service] of Object.entries(SERVICES)) {
    results[key] = await checkServiceHealth(service);
  }
  
  return results;
};

// Docker container'ı başlat
export const startDockerContainer = async (containerName) => {
  try {
    const response = await axios.post(`http://localhost:3001/api/docker/start/${containerName}`, {}, { timeout: 10000 });
    return { success: true, message: response.data.message || 'Container started' };
  } catch (error) {
    return { success: false, message: error.response?.data?.error || error.message || 'Failed to start container' };
  }
};

// Docker container'ı durdur
export const stopDockerContainer = async (containerName) => {
  try {
    const response = await axios.post(`http://localhost:3001/api/docker/stop/${containerName}`, {}, { timeout: 10000 });
    return { success: true, message: response.data.message || 'Container stopped' };
  } catch (error) {
    return { success: false, message: error.response?.data?.error || error.message || 'Failed to stop container' };
  }
};

// Docker container'ı yeniden başlat
export const restartDockerContainer = async (containerName) => {
  try {
    const response = await axios.post(`http://localhost:3001/api/docker/restart/${containerName}`, {}, { timeout: 10000 });
    return { success: true, message: response.data.message || 'Container restarted' };
  } catch (error) {
    return { success: false, message: error.response?.data?.error || error.message || 'Failed to restart container' };
  }
};

// .NET servisi başlat
export const startDotNetService = async (projectPath, port) => {
  try {
    const response = await axios.post(`http://localhost:3001/api/dotnet/start`, 
      { projectPath, port }, 
      { timeout: 15000 }
    );
    return { success: true, message: response.data.message || '.NET service started' };
  } catch (error) {
    return { success: false, message: error.response?.data?.error || error.message || 'Failed to start .NET service' };
  }
};

// .NET servisi durdur
export const stopDotNetService = async (port) => {
  try {
    const response = await axios.post(`http://localhost:3001/api/dotnet/stop`, 
      { port }, 
      { timeout: 10000 }
    );
    return { success: true, message: response.data.message || '.NET service stopped' };
  } catch (error) {
    return { success: false, message: error.response?.data?.error || error.message || 'Failed to stop .NET service' };
  }
};

// .NET servis durumunu kontrol et
export const checkDotNetServiceStatus = async (port) => {
  try {
    const response = await axios.get(`http://localhost:3001/api/dotnet/status/${port}`, { timeout: 3000 });
    return { running: response.data.running, message: response.data.message };
  } catch (error) {
    return { running: false, message: 'Status check failed' };
  }
};

// Kategoriye göre servisleri grupla
export const getServicesByCategory = () => {
  const grouped = {};
  
  Object.values(SERVICES).forEach(service => {
    if (!grouped[service.category]) {
      grouped[service.category] = [];
    }
    grouped[service.category].push(service);
  });
  
  return grouped;
};
