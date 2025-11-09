import React, { useState } from 'react';
import ConnectionForm from '../../components/DataAnalysis/ConnectionForm';
import TableList from '../../components/DataAnalysis/TableList';
import TableSchema from '../../components/DataAnalysis/TableSchema';
import AIAnalysis from '../../components/DataAnalysis/AIAnalysis';
import './DataAnalysisPage.css';

const DataAnalysisPage = () => {
  const [connectionInfo, setConnectionInfo] = useState(null);
  const [selectedTable, setSelectedTable] = useState(null);
  const [analysisData, setAnalysisData] = useState(null);
  const [currentStep, setCurrentStep] = useState(1);

  const handleConnectionSuccess = (connInfo) => {
    console.log('Connection successful:', connInfo);
    setConnectionInfo(connInfo);
    setCurrentStep(2);
  };

  const handleTableSelect = (table) => {
    setSelectedTable(table);
    setCurrentStep(3);
  };

  const handleSendToAI = (data) => {
    setAnalysisData(data);
    setCurrentStep(4);
  };

  const handleReset = () => {
    setConnectionInfo(null);
    setSelectedTable(null);
    setAnalysisData(null);
    setCurrentStep(1);
  };

  return (
    <div className="data-analysis-page">
      <div className="page-header">
        <div className="header-content">
          <h1>🔍 Veri Analizi ve AI Entegrasyonu</h1>
          <p>Müşteri veritabanlarına bağlanın ve AI ile analiz edin</p>
        </div>
        
        {connectionInfo && (
          <button className="btn btn-reset" onClick={handleReset}>
            <i className="ti ti-refresh"></i> Yeni Bağlantı
          </button>
        )}
      </div>

      <div className="progress-steps">
        <div className={`step ${currentStep >= 1 ? 'active' : ''} ${currentStep > 1 ? 'completed' : ''}`}>
          <div className="step-number">1</div>
          <div className="step-label">SQL Bağlantısı</div>
        </div>
        <div className="step-line"></div>
        <div className={`step ${currentStep >= 2 ? 'active' : ''} ${currentStep > 2 ? 'completed' : ''}`}>
          <div className="step-number">2</div>
          <div className="step-label">Tablo Seçimi</div>
        </div>
        <div className="step-line"></div>
        <div className={`step ${currentStep >= 3 ? 'active' : ''} ${currentStep > 3 ? 'completed' : ''}`}>
          <div className="step-number">3</div>
          <div className="step-label">Şema İnceleme</div>
        </div>
        <div className="step-line"></div>
        <div className={`step ${currentStep >= 4 ? 'active' : ''}`}>
          <div className="step-number">4</div>
          <div className="step-label">AI Analizi</div>
        </div>
      </div>

      <div className="page-content">
        {!connectionInfo ? (
          <ConnectionForm onConnectionSuccess={handleConnectionSuccess} />
        ) : (
          <>
            <div className="connection-info-banner">
              <i className="ti ti-check-circle"></i>
              <span>Bağlantı: {connectionInfo.host} - {connectionInfo.database}</span>
            </div>
            
            <TableList 
              connectionInfo={connectionInfo}
              onTableSelect={handleTableSelect}
            />

            {selectedTable && (
              <TableSchema
                table={selectedTable}
                connectionInfo={connectionInfo}
                onSendToAI={handleSendToAI}
              />
            )}

            {analysisData && (
              <AIAnalysis data={analysisData} />
            )}
          </>
        )}
      </div>

      <div className="page-footer">
        <div className="footer-info">
          <div className="info-item">
            <i className="ti ti-database"></i>
            <span>Data Analysis API</span>
            <span className="status-dot online"></span>
          </div>
          <div className="info-item">
            <i className="ti ti-brain"></i>
            <span>AI Services Ready</span>
            <span className="status-dot online"></span>
          </div>
        </div>
      </div>
    </div>
  );
};

export default DataAnalysisPage;
