import React, { useState } from 'react';
import { analyzeSchema, trainModel } from '../../services/dataAnalysisService';
import './AIAnalysis.css';

const AIAnalysis = ({ data }) => {
  const [activeService, setActiveService] = useState(null);
  const [isAnalyzing, setIsAnalyzing] = useState(false);
  const [results, setResults] = useState(null);
  const [error, setError] = useState('');

  const aiServices = [
    {
      id: 'schema-analyzer',
      name: 'Schema Analyzer',
      icon: 'ti-topology-star',
      color: 'var(--accent)',
      description: 'GPT-4o-mini ile veritabanı şeması analizi',
      port: 8001,
      action: analyzeSchema
    },
    {
      id: 'pycaret-engine',
      name: 'PyCaret AutoML',
      icon: 'ti-chart-line',
      color: 'var(--warning)',
      description: 'Otomatik makine öğrenimi modeli eğitimi',
      port: 8002,
      action: trainModel
    },
    {
      id: 'django-ai',
      name: 'Django AI Service',
      icon: 'ti-robot',
      color: 'var(--success)',
      description: 'WebSocket ve RabbitMQ ile gerçek zamanlı analiz',
      port: 8000,
      action: null // TODO: Implement
    }
  ];

  const handleAnalyze = async (service) => {
    setActiveService(service.id);
    setIsAnalyzing(true);
    setError('');
    setResults(null);

    try {
      if (!service.action) {
        throw new Error('Bu servis henüz entegre edilmedi');
      }

      const result = await service.action(data);
      setResults(result);
    } catch (err) {
      setError(`${service.name} hatası: ${err.message}`);
    } finally {
      setIsAnalyzing(false);
    }
  };

  return (
    <div className="ai-analysis-container">
      <div className="ai-header">
        <h3>🤖 AI Analiz Servisleri</h3>
        <p>Tablonuzu analiz etmek için bir AI servisi seçin</p>
      </div>

      <div className="ai-services-grid">
        {aiServices.map((service) => (
          <div
            key={service.id}
            className={`ai-service-card ${activeService === service.id ? 'active' : ''}`}
            style={{ '--service-color': service.color }}
          >
            <div className="service-header">
              <div className="service-icon">
                <i className={`ti ${service.icon}`}></i>
              </div>
              <div className="service-info">
                <h4>{service.name}</h4>
                <p>{service.description}</p>
                <span className="service-port">Port: {service.port}</span>
              </div>
            </div>

            <button
              className="btn btn-analyze"
              onClick={() => handleAnalyze(service)}
              disabled={isAnalyzing || !data}
            >
              {isAnalyzing && activeService === service.id ? (
                <>
                  <span className="spinner"></span> Analiz Ediliyor...
                </>
              ) : (
                <>
                  <i className="ti ti-play"></i> Analizi Başlat
                </>
              )}
            </button>
          </div>
        ))}
      </div>

      {error && (
        <div className="analysis-error">
          <i className="ti ti-alert-circle"></i>
          <p>{error}</p>
        </div>
      )}

      {results && (
        <div className="analysis-results">
          <div className="results-header">
            <i className="ti ti-check-circle"></i>
            <h4>Analiz Sonuçları</h4>
          </div>
          <pre>{JSON.stringify(results, null, 2)}</pre>
        </div>
      )}

      {!data && (
        <div className="no-data-warning">
          <i className="ti ti-info-circle"></i>
          <p>Lütfen önce bir tablo seçin ve şemasını görüntüleyin</p>
        </div>
      )}
    </div>
  );
};

export default AIAnalysis;
