import { useState, useEffect } from 'react';
import { useAIWebSocket, useAIHealthStatus } from '../../hooks/useAIWebSocket';
import { schemaAnalyzerAPI, pycaretEngineAPI } from '../../services/aiService';
import './AIDashboard.css';

export default function AIDashboard({ companyId }) {
  const [activeTab, setActiveTab] = useState('realtime');
  const [connectionInfo, setConnectionInfo] = useState({
    db_type: 'postgresql',
    host: 'localhost',
    port: '5432',
    database: '',
    username: '',
    password: ''
  });
  const [schemaAnalysis, setSchemaAnalysis] = useState(null);
  const [isAnalyzing, setIsAnalyzing] = useState(false);
  const [trainingStatus, setTrainingStatus] = useState(null);

  const { 
    isConnected, 
    predictions, 
    error: wsError,
    clearPredictions 
  } = useAIWebSocket(companyId);

  const healthStatus = useAIHealthStatus();

  const handleTestConnection = async () => {
    try {
      setIsAnalyzing(true);
      const result = await schemaAnalyzerAPI.testConnection(connectionInfo);
      alert(result.success ? 'Connection successful!' : 'Connection failed!');
    } catch (error) {
      alert('Connection test failed: ' + error.message);
    } finally {
      setIsAnalyzing(false);
    }
  };

  const handleAnalyzeSchema = async () => {
    try {
      setIsAnalyzing(true);
      const result = await schemaAnalyzerAPI.analyzeSchema(connectionInfo);
      setSchemaAnalysis(result);
      setActiveTab('schema');
    } catch (error) {
      alert('Schema analysis failed: ' + error.message);
    } finally {
      setIsAnalyzing(false);
    }
  };

  const handleTrainModel = async () => {
    if (!schemaAnalysis) {
      alert('Please analyze schema first!');
      return;
    }

    try {
      const trainingData = {
        company_id: companyId,
        connection_info: connectionInfo,
        target_column: schemaAnalysis.tables[0]?.columns[0]?.name, // Example
        task_type: 'regression'
      };

      const result = await pycaretEngineAPI.trainModel(trainingData);
      setTrainingStatus({ jobId: result.job_id, status: 'training' });
      
      // Poll for training status
      const pollInterval = setInterval(async () => {
        const status = await pycaretEngineAPI.getTrainingStatus(result.job_id);
        setTrainingStatus(status);
        
        if (status.status === 'completed' || status.status === 'failed') {
          clearInterval(pollInterval);
        }
      }, 5000);
    } catch (error) {
      alert('Model training failed: ' + error.message);
    }
  };

  return (
    <div className="ai-dashboard">
      {/* Header with Health Status */}
      <div className="dashboard-header">
        <h2>AI Dashboard - Company {companyId}</h2>
        <div className="health-indicators">
          <HealthIndicator 
            label="Schema Analyzer" 
            status={healthStatus.schemaAnalyzer} 
          />
          <HealthIndicator 
            label="PyCaret Engine" 
            status={healthStatus.pycaretEngine} 
          />
          <HealthIndicator 
            label="Django AI" 
            status={healthStatus.djangoAI} 
          />
          <HealthIndicator 
            label="WebSocket" 
            status={isConnected ? 'healthy' : 'offline'} 
          />
        </div>
      </div>

      {/* Tabs */}
      <div className="dashboard-tabs">
        <button 
          className={activeTab === 'realtime' ? 'active' : ''} 
          onClick={() => setActiveTab('realtime')}
        >
          Real-time Predictions
        </button>
        <button 
          className={activeTab === 'connection' ? 'active' : ''} 
          onClick={() => setActiveTab('connection')}
        >
          Database Connection
        </button>
        <button 
          className={activeTab === 'schema' ? 'active' : ''} 
          onClick={() => setActiveTab('schema')}
        >
          Schema Analysis
        </button>
        <button 
          className={activeTab === 'training' ? 'active' : ''} 
          onClick={() => setActiveTab('training')}
        >
          Model Training
        </button>
      </div>

      {/* Tab Content */}
      <div className="dashboard-content">
        {activeTab === 'realtime' && (
          <RealtimePanel 
            predictions={predictions} 
            isConnected={isConnected}
            error={wsError}
            onClear={clearPredictions}
          />
        )}

        {activeTab === 'connection' && (
          <ConnectionPanel 
            connectionInfo={connectionInfo}
            onChange={setConnectionInfo}
            onTest={handleTestConnection}
            onAnalyze={handleAnalyzeSchema}
            isAnalyzing={isAnalyzing}
          />
        )}

        {activeTab === 'schema' && (
          <SchemaPanel 
            schemaAnalysis={schemaAnalysis}
          />
        )}

        {activeTab === 'training' && (
          <TrainingPanel 
            trainingStatus={trainingStatus}
            onTrain={handleTrainModel}
            hasSchema={!!schemaAnalysis}
          />
        )}
      </div>
    </div>
  );
}

// Sub-components
function HealthIndicator({ label, status }) {
  const getStatusClass = () => {
    switch (status) {
      case 'healthy': return 'status-healthy';
      case 'unhealthy': return 'status-unhealthy';
      case 'offline': return 'status-offline';
      default: return 'status-unknown';
    }
  };

  return (
    <div className={`health-indicator ${getStatusClass()}`}>
      <span className="indicator-dot"></span>
      <span className="indicator-label">{label}</span>
    </div>
  );
}

function RealtimePanel({ predictions, isConnected, error, onClear }) {
  return (
    <div className="realtime-panel">
      <div className="panel-header">
        <h3>Real-time Predictions</h3>
        <button onClick={onClear} className="btn-secondary">Clear</button>
      </div>

      {error && (
        <div className="alert alert-danger">{error}</div>
      )}

      {!isConnected && (
        <div className="alert alert-warning">
          WebSocket not connected. Waiting for connection...
        </div>
      )}

      <div className="predictions-list">
        {predictions.length === 0 ? (
          <div className="empty-state">
            No predictions yet. Waiting for real-time data...
          </div>
        ) : (
          predictions.map((pred, index) => (
            <div key={index} className="prediction-card">
              <div className="prediction-time">
                {new Date(pred.timestamp).toLocaleString()}
              </div>
              <div className="prediction-value">
                Prediction: <strong>{pred.prediction?.toFixed(2) || 'N/A'}</strong>
              </div>
              {pred.confidence && (
                <div className="prediction-confidence">
                  Confidence: {(pred.confidence * 100).toFixed(1)}%
                </div>
              )}
            </div>
          ))
        )}
      </div>
    </div>
  );
}

function ConnectionPanel({ connectionInfo, onChange, onTest, onAnalyze, isAnalyzing }) {
  const handleChange = (field, value) => {
    onChange({ ...connectionInfo, [field]: value });
  };

  return (
    <div className="connection-panel">
      <h3>Database Connection</h3>
      
      <div className="form-group">
        <label>Database Type</label>
        <select 
          value={connectionInfo.db_type}
          onChange={(e) => handleChange('db_type', e.target.value)}
        >
          <option value="postgresql">PostgreSQL</option>
          <option value="mysql">MySQL</option>
          <option value="mssql">MS SQL Server</option>
        </select>
      </div>

      <div className="form-row">
        <div className="form-group">
          <label>Host</label>
          <input 
            type="text"
            value={connectionInfo.host}
            onChange={(e) => handleChange('host', e.target.value)}
            placeholder="localhost"
          />
        </div>
        <div className="form-group">
          <label>Port</label>
          <input 
            type="text"
            value={connectionInfo.port}
            onChange={(e) => handleChange('port', e.target.value)}
            placeholder="5432"
          />
        </div>
      </div>

      <div className="form-group">
        <label>Database Name</label>
        <input 
          type="text"
          value={connectionInfo.database}
          onChange={(e) => handleChange('database', e.target.value)}
          placeholder="my_database"
        />
      </div>

      <div className="form-group">
        <label>Username</label>
        <input 
          type="text"
          value={connectionInfo.username}
          onChange={(e) => handleChange('username', e.target.value)}
          placeholder="postgres"
        />
      </div>

      <div className="form-group">
        <label>Password</label>
        <input 
          type="password"
          value={connectionInfo.password}
          onChange={(e) => handleChange('password', e.target.value)}
          placeholder="••••••••"
        />
      </div>

      <div className="button-group">
        <button 
          onClick={onTest}
          disabled={isAnalyzing}
          className="btn-secondary"
        >
          Test Connection
        </button>
        <button 
          onClick={onAnalyze}
          disabled={isAnalyzing}
          className="btn-primary"
        >
          {isAnalyzing ? 'Analyzing...' : 'Analyze Schema'}
        </button>
      </div>
    </div>
  );
}

function SchemaPanel({ schemaAnalysis }) {
  if (!schemaAnalysis) {
    return (
      <div className="schema-panel empty">
        <p>No schema analysis available. Please analyze a database first.</p>
      </div>
    );
  }

  return (
    <div className="schema-panel">
      <h3>Schema Analysis Results</h3>
      
      <div className="schema-summary">
        <div className="summary-card">
          <h4>{schemaAnalysis.tables?.length || 0}</h4>
          <p>Tables</p>
        </div>
        <div className="summary-card">
          <h4>{schemaAnalysis.total_columns || 0}</h4>
          <p>Total Columns</p>
        </div>
      </div>

      <div className="tables-list">
        {schemaAnalysis.tables?.map((table, index) => (
          <div key={index} className="table-card">
            <h4>{table.name}</h4>
            <p className="table-description">{table.semantic_meaning}</p>
            <div className="columns-grid">
              {table.columns?.map((col, colIndex) => (
                <div key={colIndex} className="column-item">
                  <strong>{col.name}</strong>
                  <span className="column-type">{col.type}</span>
                  {col.semantic_meaning && (
                    <p className="column-description">{col.semantic_meaning}</p>
                  )}
                </div>
              ))}
            </div>
          </div>
        ))}
      </div>
    </div>
  );
}

function TrainingPanel({ trainingStatus, onTrain, hasSchema }) {
  return (
    <div className="training-panel">
      <h3>Model Training</h3>

      {!hasSchema && (
        <div className="alert alert-info">
          Please analyze a database schema first before training a model.
        </div>
      )}

      {trainingStatus && (
        <div className="training-status">
          <h4>Training Status: {trainingStatus.status}</h4>
          <p>Job ID: {trainingStatus.jobId}</p>
          {trainingStatus.metrics && (
            <div className="metrics">
              <h5>Model Metrics:</h5>
              <pre>{JSON.stringify(trainingStatus.metrics, null, 2)}</pre>
            </div>
          )}
        </div>
      )}

      <button 
        onClick={onTrain}
        disabled={!hasSchema || trainingStatus?.status === 'training'}
        className="btn-primary"
      >
        {trainingStatus?.status === 'training' ? 'Training...' : 'Train New Model'}
      </button>
    </div>
  );
}
