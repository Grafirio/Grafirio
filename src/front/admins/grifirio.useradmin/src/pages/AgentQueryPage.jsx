import React, { useState, useEffect, useRef, useCallback } from 'react';
import { useSearchParams } from 'react-router-dom';
import {
  IconBrain, IconSend, IconLoader2, IconCheck, IconAlertCircle,
  IconChartBar, IconChartPie, IconChartLine, IconHistory,
  IconDatabase, IconArrowLeft, IconSparkles, IconRefresh, IconUser, IconRobot
} from '@tabler/icons-react';
import {
  analyzeConnectionSchema, getAgentConfig, getAgentConfigStatus,
  submitAgentQuery, getAgentQueryStatus, getAgentQueryResult,
  getAgentQueryHistory, getSavedConnections
} from '../services/dataAnalysisService';
import {
  Chart as ChartJS,
  CategoryScale, LinearScale, PointElement, LineElement,
  BarElement, ArcElement, RadialLinearScale,
  Title, Tooltip, Legend, Filler
} from 'chart.js';
import { Bar, Line, Pie, Doughnut, Radar } from 'react-chartjs-2';
import '../styles/AgentQueryPage.css';

ChartJS.register(
  CategoryScale, LinearScale, PointElement, LineElement,
  BarElement, ArcElement, RadialLinearScale,
  Title, Tooltip, Legend, Filler
);

/**
 * Sunucudan gelen ham hata metnini kullanicinin bir sey yapabilecegi bir
 * cumleye cevirir. Ham metin de korunuyor: cogu durumda asil bilgi orada
 * (hangi kolon, hangi tablo) ve gizlemek teshisi imkansizlastiriyor.
 *
 * En sik gorulen durum, LLM'in var olmayan bir kolon adi uretmesi. Bu
 * kullanici hatasi degil ama kullanicinin duzeltebilecegi bir sey: soruyu
 * dogru kolon adiyla tekrar sormasi yetiyor.
 */
const describeAnalysisFailure = (raw) => {
  if (!raw) return 'Analiz başarısız oldu. Sunucu bir sebep bildirmedi.';

  const column = raw.match(/Invalid column name '([^']+)'/i);
  if (column) {
    return `Veritabanında '${column[1]}' adında bir kolon yok. Sorunuzda geçen alan adını kontrol edip tekrar deneyin.`;
  }

  const table = raw.match(/Invalid object name '([^']+)'/i);
  if (table) {
    return `Veritabanında '${table[1]}' adında bir tablo yok. Bağlantının tablo seçimini kontrol edin.`;
  }

  if (/login failed|authentication|password/i.test(raw)) {
    return 'Veritabanı kimlik doğrulaması başarısız. Bağlantı ayarlarındaki kullanıcı ve şifreyi kontrol edin.';
  }

  if (/timeout|timed out/i.test(raw)) {
    return 'Veritabanı zaman aşımına uğradı. Sunucuya erişilebildiğinden emin olun.';
  }

  return raw.length > 300 ? `${raw.slice(0, 300)}…` : raw;
};

const AgentQueryPage = () => {
  const [searchParams] = useSearchParams();
  const initialConnectionId = searchParams.get('connectionId');

  // State
  const [connections, setConnections] = useState([]);
  const [selectedConnectionId, setSelectedConnectionId] = useState(initialConnectionId || '');
  const [configStatus, setConfigStatus] = useState(null); // none, analyzing, ready, failed
  const [configData, setConfigData] = useState(null);
  const [isAnalyzing, setIsAnalyzing] = useState(false);

  const [question, setQuestion] = useState('');
  const [isQuerying, setIsQuerying] = useState(false);
  const [activeQueryId, setActiveQueryId] = useState(null);
  const [messages, setMessages] = useState([]); // Chat messages: {role: 'user'|'ai', content, result, timestamp}
  const [selectedResult, setSelectedResult] = useState(null); // Currently selected result for right panel

  const [error, setError] = useState('');
  const questionInputRef = useRef(null);
  const pollingRef = useRef(null);
  const chatEndRef = useRef(null);

  // Load saved connections
  useEffect(() => {
    const loadConnections = async () => {
      try {
        const userId = 'user-123'; // TODO: Gerçek userId
        const result = await getSavedConnections();
        if (result.success && result.connections) {
          setConnections(result.connections);
        }
      } catch (err) {
        console.error('Failed to load connections:', err);
      }
    };
    loadConnections();
  }, []);

  // Check config status when connection changes
  useEffect(() => {
    if (!selectedConnectionId) {
      setConfigStatus(null);
      setConfigData(null);
      return;
    }

    const checkConfig = async () => {
      try {
        const status = await getAgentConfigStatus(selectedConnectionId);
        setConfigStatus(status.status);

        if (status.status === 'ready') {
          const config = await getAgentConfig(selectedConnectionId);
          setConfigData(config);
          loadQueryHistory();
        }
      } catch (err) {
        setConfigStatus('none');
      }
    };

    checkConfig();
  }, [selectedConnectionId]);

  // Cleanup polling on unmount
  useEffect(() => {
    return () => {
      if (pollingRef.current) clearInterval(pollingRef.current);
    };
  }, []);

  // Auto scroll to bottom when new messages arrive
  useEffect(() => {
    chatEndRef.current?.scrollIntoView({ behavior: 'smooth' });
  }, [messages]);

  const loadQueryHistory = async () => {
    if (!selectedConnectionId) return;
    try {
      const result = await getAgentQueryHistory(selectedConnectionId);
      if (result.queries && result.queries.length > 0) {
        // Convert history to messages
        const historyMessages = [];
        for (const item of result.queries) {
          historyMessages.push({
            role: 'user',
            content: item.question,
            timestamp: item.createdAt
          });
          
          if (item.status === 'completed') {
            try {
              const queryResult = await getAgentQueryResult(item.queryId);
              historyMessages.push({
                role: 'ai',
                content: queryResult.result?.summary || 'Analiz tamamlandı',
                result: queryResult,
                timestamp: item.completedAt || item.createdAt
              });
            } catch (err) {
              console.error('Failed to load result:', err);
            }
          }
        }
        setMessages(historyMessages);
      }
    } catch (err) {
      console.error('Failed to load query history:', err);
    }
  };

  // Analyze connection schema with the configured LLM (Azure OpenAI)
  const handleAnalyze = async () => {
    if (!selectedConnectionId) return;
    setIsAnalyzing(true);
    setError('');
    setConfigStatus('analyzing');

    try {
      const result = await analyzeConnectionSchema(selectedConnectionId);

      if (result.success) {
        setConfigStatus('ready');
        setConfigData(result);
        loadQueryHistory();
      } else {
        setConfigStatus('failed');
        setError(result.error || 'Analiz başarısız');
      }
    } catch (err) {
      setConfigStatus('failed');
      setError(err.response?.data?.detail || err.message);
    } finally {
      setIsAnalyzing(false);
    }
  };

  // Submit question
  const handleSubmitQuery = async (e) => {
    e?.preventDefault();
    if (!question.trim() || !selectedConnectionId || isQuerying) return;

    const userQuestion = question.trim();
    setQuestion('');
    setError('');

    // Add user message
    const userMessage = {
      role: 'user',
      content: userQuestion,
      timestamp: new Date().toISOString()
    };
    setMessages(prev => [...prev, userMessage]);
    setIsQuerying(true);

    try {
      const result = await submitAgentQuery(selectedConnectionId, userQuestion);

      if (result.success) {
        setActiveQueryId(result.queryId);
        // Add AI loading message
        const loadingMessage = {
          role: 'ai',
          content: result.explanation || 'Analiz yapılıyor...',
          loading: true,
          timestamp: new Date().toISOString()
        };
        setMessages(prev => [...prev, loadingMessage]);
        startPolling(result.queryId);
      } else {
        setError(result.error || 'Sorgu gönderilemedi');
        setIsQuerying(false);
      }
    } catch (err) {
      setError(err.response?.data?.detail || err.message);
      setIsQuerying(false);
    }
  };

  // Poll for query result
  const startPolling = useCallback((queryId) => {
    if (pollingRef.current) clearInterval(pollingRef.current);

    pollingRef.current = setInterval(async () => {
      try {
        const status = await getAgentQueryStatus(queryId);

        if (status.status === 'completed') {
          clearInterval(pollingRef.current);
          pollingRef.current = null;

          const result = await getAgentQueryResult(queryId);
          
          // Replace loading message with actual result
          setMessages(prev => {
            const newMessages = [...prev];
            const lastIdx = newMessages.length - 1;
            if (newMessages[lastIdx]?.role === 'ai' && newMessages[lastIdx]?.loading) {
              newMessages[lastIdx] = {
                role: 'ai',
                content: result.result?.summary || 'Analiz tamamlandı.',
                result: result,
                timestamp: new Date().toISOString()
              };
            }
            return newMessages;
          });

          setSelectedResult(result);
          setIsQuerying(false);
          setActiveQueryId(null);
        } else if (status.status === 'failed') {
          clearInterval(pollingRef.current);
          pollingRef.current = null;

          // Sunucu artik sebebi de donuyor. Sabit "başarısız oldu" cumlesi
          // kullaniciya hicbir sey soylemiyordu; oysa sebep cogu zaman
          // kullanicinin kendi duzeltebilecegi bir sey oluyor (ornegin
          // olmayan bir kolon adi).
          const reason = describeAnalysisFailure(status.error);

          setMessages(prev => {
            const newMessages = [...prev];
            const lastIdx = newMessages.length - 1;
            if (newMessages[lastIdx]?.role === 'ai' && newMessages[lastIdx]?.loading) {
              newMessages[lastIdx] = {
                role: 'ai',
                content: `❌ ${reason}`,
                error: true,
                timestamp: new Date().toISOString()
              };
            }
            return newMessages;
          });

          setError(reason);
          setIsQuerying(false);
          setActiveQueryId(null);
        }
      } catch (err) {
        console.error('Polling error:', err);
      }
    }, 3000);
  }, [selectedConnectionId]);

  // Load a previous query result into right panel
  const handleSelectMessage = (message) => {
    if (message.role === 'ai' && message.result) {
      setSelectedResult(message.result);
    }
  };

  // Quick question
  const handleQuickQuestion = (q) => {
    setQuestion(q);
  };

  // Chart rendering
  const renderChart = (chart, index) => {
    const colors = [
      'rgba(59, 130, 246, 0.8)',
      'rgba(16, 185, 129, 0.8)',
      'rgba(245, 158, 11, 0.8)',
      'rgba(239, 68, 68, 0.8)',
      'rgba(139, 92, 246, 0.8)',
      'rgba(236, 72, 153, 0.8)',
      'rgba(14, 165, 233, 0.8)',
      'rgba(34, 197, 94, 0.8)',
    ];

    const chartData = {
      labels: chart.data?.labels || [],
      datasets: (chart.data?.datasets || []).map((ds, i) => ({
        label: ds.label || 'Veri',
        data: ds.data || [],
        backgroundColor: chart.type === 'line'
          ? 'rgba(59, 130, 246, 0.1)'
          : ds.backgroundColor || colors,
        borderColor: chart.type === 'line'
          ? 'rgba(59, 130, 246, 1)'
          : ds.borderColor || 'rgba(255, 255, 255, 0.8)',
        borderWidth: chart.type === 'line' ? 2 : 1,
        fill: chart.type === 'line',
        tension: 0.4,
      }))
    };

    const options = {
      responsive: true,
      maintainAspectRatio: false,
      plugins: {
        legend: { display: true, position: 'top' },
        title: { display: true, text: chart.title || '', font: { size: 14 } },
        tooltip: {
          backgroundColor: 'rgba(0,0,0,0.8)',
          padding: 12,
          titleColor: '#fff',
          bodyColor: '#fff',
        }
      },
      scales: ['pie', 'doughnut', 'radar'].includes(chart.type?.toLowerCase()) ? {} : {
        y: { beginAtZero: true, grid: { color: 'rgba(0,0,0,0.05)' } },
        x: { grid: { display: false } }
      }
    };

    const ChartComponent = {
      bar: Bar, line: Line, pie: Pie, doughnut: Doughnut, radar: Radar
    }[chart.type?.toLowerCase()] || Bar;

    return (
      <div key={index} className="agent-chart-card">
        <div className="chart-wrapper">
          <ChartComponent data={chartData} options={options} />
        </div>
      </div>
    );
  };

  const selectedConn = connections.find(c => c.id === selectedConnectionId);

  return (
    <div className="agent-query-page">
      {/* Header — baslik blogu kaldirildi: sayfanin ne oldugunu zaten menu
          soyluyor, o alan da dikey yerin dortte birini yiyordu. Geriye
          yalnizca islevi olan iki sey kaldi: baglanti secimi ve durum. */}
      <div className="agent-header">
        <div className="agent-header-content">
          {/* Connection Selector */}
          <div className="header-connection-selector">
            <select
              className="connection-select-compact"
              value={selectedConnectionId}
              onChange={(e) => {
                setSelectedConnectionId(e.target.value);
                setMessages([]);
                setSelectedResult(null);
                setQuestion('');
                setError('');
              }}
            >
              <option value="">Bağlantı seçin...</option>
              {connections.map(conn => (
                <option key={conn.id} value={conn.id}>
                  {conn.name} - {conn.database}
                </option>
              ))}
            </select>

            {/* Analyze button next to selector */}
            {selectedConnectionId && configStatus !== 'ready' && (
              <button
                className="btn-analyze-compact"
                onClick={handleAnalyze}
                disabled={isAnalyzing}
                title="Schema'yı analiz et"
              >
                {isAnalyzing ? (
                  <IconLoader2 className="spin" size={18} />
                ) : (
                  <IconSparkles size={18} />
                )}
              </button>
            )}
          </div>
        </div>

        {/* Config Status Bar */}
        {selectedConnectionId && (
          <div className="config-status-bar">
            {configStatus === 'analyzing' ? (
              <div className="status-msg analyzing">
                <IconLoader2 className="spin" size={16} />
                <span>Schema analiz ediliyor...</span>
              </div>
            ) : configStatus === 'ready' ? (
              <div className="status-msg ready">
                <IconCheck size={16} />
                <span>Hazır - AI asistana soru sorabilirsiniz</span>
              </div>
            ) : configStatus === 'failed' ? (
              <div className="status-msg failed">
                <IconAlertCircle size={16} />
                <span>Analiz başarısız</span>
                <button className="retry-btn" onClick={handleAnalyze}>
                  <IconRefresh size={14} /> Tekrar
                </button>
              </div>
            ) : null}
          </div>
        )}
      </div>

      {/* Error banner */}
      {error && (
        <div className="agent-error-banner">
          <IconAlertCircle size={20} />
          <span>{error}</span>
          <button onClick={() => setError('')}>✕</button>
        </div>
      )}

      {configStatus === 'ready' ? (
        <div className="agent-workspace">
          {/* Left Panel - Chat */}
          <div className="chat-panel">
            <div className="chat-messages">
              {messages.length === 0 ? (
                <div className="chat-welcome">
                  <IconBrain size={48} />
                  <h3>Hoş geldiniz!</h3>
                  <p>Verileriniz hakkında soru sormaya başlayın.</p>
                  <div className="welcome-suggestions">
                    <p className="suggestion-label">Örnek sorular:</p>
                    {[
                      'En çok satılan ürünler neler?',
                      'Aylık gelir trendi nasıl?',
                      'Müşteri segmentasyonu yap',
                    ].map((q, i) => (
                      <button
                        key={i}
                        className="suggestion-btn"
                        onClick={() => handleQuickQuestion(q)}
                      >
                        {q}
                      </button>
                    ))}
                  </div>
                </div>
              ) : (
                messages.map((msg, idx) => (
                  <div
                    key={idx}
                    className={`chat-message ${msg.role}`}
                    onClick={() => handleSelectMessage(msg)}
                  >
                    <div className="message-avatar">
                      {msg.role === 'user' ? (
                        <IconUser size={20} />
                      ) : (
                        <IconRobot size={20} />
                      )}
                    </div>
                    <div className="message-content">
                      <div className="message-text">
                        {msg.loading ? (
                          <div className="typing-indicator">
                            <span></span><span></span><span></span>
                          </div>
                        ) : (
                          msg.content
                        )}
                      </div>
                      {msg.result && (
                        <div className="message-meta">
                          <IconChartBar size={14} />
                          <span>{msg.result?.result?.charts?.length || 0} grafik</span>
                        </div>
                      )}
                    </div>
                  </div>
                ))
              )}
              <div ref={chatEndRef} />
            </div>

            {/* Chat Input */}
            <form onSubmit={handleSubmitQuery} className="chat-input-form">
              <div className="chat-input-wrapper">
                <input
                  ref={questionInputRef}
                  type="text"
                  className="chat-input"
                  placeholder={selectedConn ? `"${selectedConn.database}" hakkında soru sorun...` : 'Soru sorun...'}
                  value={question}
                  onChange={(e) => setQuestion(e.target.value)}
                  disabled={isQuerying}
                />
                <button
                  type="submit"
                  className="chat-submit-btn"
                  disabled={!question.trim() || isQuerying}
                >
                  {isQuerying ? (
                    <IconLoader2 className="spin" size={20} />
                  ) : (
                    <IconSend size={20} />
                  )}
                </button>
              </div>
            </form>
          </div>

          {/* Right Panel - Results */}
          <div className="results-panel">
            {selectedResult ? (
              <>
                <div className="results-header">
                  <h3>📊 Analiz Sonuçları</h3>
                  <span className="result-question">"{selectedResult.question}"</span>
                </div>

                {/* Summary */}
                {selectedResult.result?.summary && (
                  <div className="result-summary">
                    <p>{selectedResult.result.summary}</p>
                  </div>
                )}

                {/* Charts */}
                {selectedResult.result?.charts && selectedResult.result.charts.length > 0 && (
                  <div className="charts-container">
                    {(typeof selectedResult.result.charts === 'string'
                      ? JSON.parse(selectedResult.result.charts)
                      : selectedResult.result.charts
                    ).map((chart, index) => renderChart(chart, index))}
                  </div>
                )}

                {/* Insights */}
                {selectedResult.result?.insights && (
                  <div className="insights-section">
                    <h4>💡 Önemli Bulgular</h4>
                    <div className="insights-list">
                      {(typeof selectedResult.result.insights === 'string'
                        ? JSON.parse(selectedResult.result.insights)
                        : selectedResult.result.insights
                      ).map((insight, index) => (
                        <div key={index} className={`insight-item insight-${insight.type}`}>
                          <div className="insight-icon">
                            {insight.type === 'success' && '✓'}
                            {insight.type === 'warning' && '⚠'}
                            {insight.type === 'info' && 'ℹ'}
                            {insight.type === 'metric' && '📊'}
                          </div>
                          <div>
                            <strong>{insight.title}</strong>
                            <p>{insight.description}</p>
                          </div>
                        </div>
                      ))}
                    </div>
                  </div>
                )}
              </>
            ) : (
              <div className="results-placeholder">
                <IconChartLine size={64} />
                <h3>Grafikler burada görünecek</h3>
                <p>Sol panelden bir soru seçin veya yeni bir soru sorun</p>
              </div>
            )}
          </div>
        </div>
      ) : (
        <div className="agent-placeholder">
          <IconDatabase size={64} />
          <h3>
            {!selectedConnectionId
              ? 'Veritabanı Seçin'
              : configStatus === null || configStatus === 'none'
              ? 'Schema Analizi Gerekli'
              : 'Analiz Yapılıyor...'}
          </h3>
          <p>
            {!selectedConnectionId
              ? 'Analiz etmek istediğiniz bağlantıyı yukarıdan seçin'
              : configStatus === null || configStatus === 'none'
              ? '"Analiz Et" butonuna tıklayarak başlayın'
              : 'Lütfen bekleyin...'}
          </p>
        </div>
      )}
    </div>
  );
};

export default AgentQueryPage;
