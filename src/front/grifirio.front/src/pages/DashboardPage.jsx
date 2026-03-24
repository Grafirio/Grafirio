import React, { useState, useEffect, useRef } from 'react';
import { IconRefresh, IconCheck, IconClock, IconAlertCircle, IconLoader2, IconChartBar, IconUser, IconRobot, IconSend } from '@tabler/icons-react';
import { getAnalysisStatus, generateAIReport, askAIQuestion } from '../services/dataAnalysisService';
import {
  Chart as ChartJS,
  CategoryScale,
  LinearScale,
  PointElement,
  LineElement,
  BarElement,
  ArcElement,
  RadialLinearScale,
  Title,
  Tooltip,
  Legend,
  Filler
} from 'chart.js';
import { Bar, Line, Pie, Doughnut, Radar } from 'react-chartjs-2';
import '../styles/DashboardPage.css';

// Chart.js kayıt
ChartJS.register(
  CategoryScale,
  LinearScale,
  PointElement,
  LineElement,
  BarElement,
  ArcElement,
  RadialLinearScale,
  Title,
  Tooltip,
  Legend,
  Filler
);

const DashboardPage = () => {
  const [activeAnalyses, setActiveAnalyses] = useState([]);
  const [completedAnalyses, setCompletedAnalyses] = useState([]);
  const [selectedAnalysis, setSelectedAnalysis] = useState(null);
  const [currentReport, setCurrentReport] = useState(null);
  const [activeReportType, setActiveReportType] = useState(null);
  const [activeTab, setActiveTab] = useState(null); // Aktif database tab'i
  const [apiStatus, setApiStatus] = useState({ online: false, checking: true }); // API durumu
  const [loadingReport, setLoadingReport] = useState(false); // Rapor yüklenme durumu
  
  // Chat için yeni state'ler
  const [messages, setMessages] = useState([]); // {role: 'user'|'ai', content, result, timestamp}
  const [question, setQuestion] = useState('');
  const [isQuerying, setIsQuerying] = useState(false);
  const chatEndRef = useRef(null);
  const questionInputRef = useRef(null);

  // localStorage'dan aktif analizleri yükle
  useEffect(() => {
    loadActiveAnalyses();
    checkApiHealth(); // API sağlık kontrolü
    
    const interval = setInterval(() => {
      loadActiveAnalyses();
      checkApiHealth();
    }, 10000); // Her 10 saniyede bir kontrol (5'ten 10'a çıkardık)
    
    return () => clearInterval(interval);
  }, []);

  // İlk tab'i otomatik aç
  useEffect(() => {
    if (completedAnalyses.length > 0 && !activeTab) {
      setActiveTab(completedAnalyses[0].requestId);
    }
  }, [completedAnalyses]);

  // Auto scroll to bottom when new messages arrive
  useEffect(() => {
    chatEndRef.current?.scrollIntoView({ behavior: 'smooth' });
  }, [messages]);

  // Active tab değiştiğinde mesajları temizle
  useEffect(() => {
    if (activeTab) {
      setMessages([]);
      setCurrentReport(null);
      setQuestion('');
    }
  }, [activeTab]);

  const checkApiHealth = async () => {
    try {
      const response = await fetch('http://localhost:5221/health', { 
        method: 'GET',
        headers: { 'Accept': 'application/json' }
      });
      setApiStatus({ online: response.ok, checking: false });
    } catch (error) {
      setApiStatus({ online: false, checking: false });
    }
  };

  const loadActiveAnalyses = () => {
    // localStorage'dan tüm active analysis'leri al
    const stored = localStorage.getItem('activeAnalyses');
    if (stored) {
      const analyses = JSON.parse(stored);
      const active = [];
      const completed = [];

      // Her analizi kontrol et
      analyses.forEach(analysis => {
        if (analysis.status === 'completed' || analysis.status === 'failed') {
          completed.push(analysis);
        } else {
          active.push(analysis);
          // Sadece processing durumundakileri kontrol et (throttle)
          if (analysis.status === 'processing') {
            // Son çağrıdan 8 saniye geçmediyse tekrar çağırma
            const lastCheck = analysis.lastStatusCheck || 0;
            const now = Date.now();
            if (now - lastCheck > 8000) {
              checkAnalysisStatus(analysis.requestId);
            }
          }
        }
      });

      setActiveAnalyses(active);
      setCompletedAnalyses(completed.slice(-5)); // Son 5 tamamlanmış
    }
  };

  const checkAnalysisStatus = async (requestId) => {
    try {
      const result = await getAnalysisStatus(requestId);
      
      // Analizi güncelle
      const stored = localStorage.getItem('activeAnalyses');
      if (stored) {
        const analyses = JSON.parse(stored);
        const index = analyses.findIndex(a => a.requestId === requestId);
        
        if (index !== -1) {
          analyses[index] = {
            ...analyses[index],
            status: result.status,
            progress: result.progress,
            message: result.message,
            updatedAt: new Date().toISOString(),
            lastStatusCheck: Date.now() // Son kontrol zamanını kaydet
          };

          // Eğer completed ise mock data ekle
          if (result.status === 'completed') {
            analyses[index].completedAt = new Date().toISOString();
            analyses[index].charts = [
              {
                type: 'bar',
                title: 'Tablo Satır Sayıları',
                data: { labels: ['Users', 'Posts', 'Comments'], values: [150, 234, 567] }
              },
              {
                type: 'pie',
                title: 'Veri Kalitesi Dağılımı',
                data: { labels: ['İyi', 'Orta', 'Düşük'], values: [70, 20, 10] }
              }
            ];
            analyses[index].insights = [
              {
                type: 'success',
                title: 'Analiz Tamamlandı',
                description: `${analyses[index].tables?.length || 0} tablo başarıyla analiz edildi`
              }
            ];
          }

          localStorage.setItem('activeAnalyses', JSON.stringify(analyses));
          loadActiveAnalyses();
        }
      }
    } catch (error) {
      console.error('Status check error:', error);
    }
  };

  const formatDate = (dateString) => {
    return new Date(dateString).toLocaleString('tr-TR');
  };

  const handleGenerateReport = async (analysis, reportType) => {
    console.log('🔄 Generating report:', reportType, 'for analysis:', analysis);
    
    setLoadingReport(true);
    setSelectedAnalysis(analysis);
    setActiveReportType(reportType);
    setCurrentReport(null); // Önce temizle
    
    try {
      const response = await generateAIReport(
        analysis.requestId, 
        reportType, 
        analysis.database, 
        analysis.tables || []
      );

      console.log('✅ Report response:', response);
      
      if (response.success) {
        setCurrentReport(response);
      } else {
        alert(`⚠️ Rapor oluşturulamadı: ${response.message || 'Bilinmeyen hata'}`);
      }
    } catch (error) {
      console.error('❌ Report generation error:', error);
      alert(`❌ Rapor oluşturulamadı: ${error.message}`);
    } finally {
      setLoadingReport(false);
    }
  };

  const handleAskQuestion = async (analysis, userQuestion) => {
    if (!userQuestion || userQuestion.trim() === '') {
      alert('⚠️ Lütfen bir soru girin!');
      return;
    }

    const questionText = userQuestion.trim();
    
    // Add user message
    const userMessage = {
      role: 'user',
      content: questionText,
      timestamp: new Date().toISOString()
    };
    setMessages(prev => [...prev, userMessage]);
    setQuestion('');
    setIsQuerying(true);
    
    // Add AI loading message
    const loadingMessage = {
      role: 'ai',
      content: 'Analiz yapılıyor...',
      loading: true,
      timestamp: new Date().toISOString()
    };
    setMessages(prev => [...prev, loadingMessage]);
    
    try {
      const response = await askAIQuestion(
        analysis.requestId, 
        questionText, 
        analysis.database, 
        analysis.tables || []
      );

      console.log('✅ Question response:', response);
      
      if (response.success) {
        // Check if result is immediate or needs polling
        if (response.charts && response.charts.length > 0) {
          // Immediate result (mock data)
          setMessages(prev => {
            const newMessages = [...prev];
            const lastIdx = newMessages.length - 1;
            if (newMessages[lastIdx]?.role === 'ai' && newMessages[lastIdx]?.loading) {
              newMessages[lastIdx] = {
                role: 'ai',
                content: response.answer || 'Analiz tamamlandı.',
                result: response,
                timestamp: new Date().toISOString()
              };
            }
            return newMessages;
          });
          setCurrentReport(response);
        } else {
          // Start polling for result
          startPollingForResult(analysis.requestId, questionText);
        }
      } else {
        // Replace loading message with error
        setMessages(prev => {
          const newMessages = [...prev];
          const lastIdx = newMessages.length - 1;
          if (newMessages[lastIdx]?.role === 'ai' && newMessages[lastIdx]?.loading) {
            newMessages[lastIdx] = {
              role: 'ai',
              content: `❌ ${response.message || 'Soru cevaplanamadı'}`,
              error: true,
              timestamp: new Date().toISOString()
            };
          }
          return newMessages;
        });
      }
    } catch (error) {
      console.error('❌ Question error:', error);
      
      // Replace loading message with error
      setMessages(prev => {
        const newMessages = [...prev];
        const lastIdx = newMessages.length - 1;
        if (newMessages[lastIdx]?.role === 'ai' && newMessages[lastIdx]?.loading) {
          newMessages[lastIdx] = {
            role: 'ai',
            content: `❌ ${error.message || 'Bir hata oluştu'}`,
            error: true,
            timestamp: new Date().toISOString()
          };
        }
        return newMessages;
      });
    } finally {
      setIsQuerying(false);
    }
  };

  const startPollingForResult = (requestId, question) => {
    console.log('🔄 Starting polling for:', requestId);
    let attempts = 0;
    const maxAttempts = 30; // 30 seconds max
    
    const API_BASE = import.meta.env.VITE_API_URL || 'http://localhost:5000';
    
    const pollInterval = setInterval(async () => {
      attempts++;
      
      try {
        const response = await fetch(`${API_BASE}/data-analysis/api/ai/reports/status/${requestId}`);
        const data = await response.json();
        
        console.log(`🔄 Poll attempt ${attempts}:`, data);
        
        if (data.status === 'completed') {
          clearInterval(pollInterval);
          
          // Update message with result
          setMessages(prev => {
            const newMessages = [...prev];
            const lastIdx = newMessages.length - 1;
            if (newMessages[lastIdx]?.role === 'ai' && newMessages[lastIdx]?.loading) {
              newMessages[lastIdx] = {
                role: 'ai',
                content: data.result?.answer || 'Analiz tamamlandı!',
                result: data.result,
                timestamp: new Date().toISOString()
              };
            }
            return newMessages;
          });
          
          if (data.result) {
            setCurrentReport(data.result);
          }
          
          setIsQuerying(false);
        } else if (data.status === 'failed') {
          clearInterval(pollInterval);
          
          setMessages(prev => {
            const newMessages = [...prev];
            const lastIdx = newMessages.length - 1;
            if (newMessages[lastIdx]?.role === 'ai') {
              newMessages[lastIdx] = {
                role: 'ai',
                content: `❌ Analiz başarısız: ${data.result?.error || 'Bilinmeyen hata'}`,
                error: true,
                timestamp: new Date().toISOString()
              };
            }
            return newMessages;
          });
          
          setIsQuerying(false);
        } else if (data.progress) {
          // Update progress message
          setMessages(prev => {
            const newMessages = [...prev];
            const lastIdx = newMessages.length - 1;
            if (newMessages[lastIdx]?.role === 'ai' && newMessages[lastIdx]?.loading) {
              newMessages[lastIdx].content = `${data.progressMessage || 'İşleniyor...'} (${data.progress}%)`;
            }
            return newMessages;
          });
        }
        
        if (attempts >= maxAttempts) {
          clearInterval(pollInterval);
          console.warn('⚠️ Polling timeout');
          
          setMessages(prev => {
            const newMessages = [...prev];
            const lastIdx = newMessages.length - 1;
            if (newMessages[lastIdx]?.role === 'ai') {
              newMessages[lastIdx] = {
                role: 'ai',
                content: '⏱️ Zaman aşımı. Lütfen tekrar deneyin.',
                error: true,
                timestamp: new Date().toISOString()
              };
            }
            return newMessages;
          });
          
          setIsQuerying(false);
        }
      } catch (error) {
        console.error('🔄 Polling error:', error);
      }
    }, 1000); // Poll every second
  };

  const handleQuickQuestion = (analysis, questionText) => {
    setQuestion(questionText);
    setTimeout(() => {
      handleAskQuestion(analysis, questionText);
    }, 100);
  };

  // Mesaja tıklanınca sonucu göster
  const handleSelectMessage = (message) => {
    if (message.role === 'ai' && message.result) {
      setCurrentReport(message.result);
    }
  };

  // Chart render fonksiyonu
  const renderChart = (chart, index) => {
    const chartData = {
      labels: chart.data.labels,
      datasets: [{
        label: chart.data.datasets?.[0]?.label || 'Veri',
        data: chart.data.datasets?.[0]?.data || [],
        backgroundColor: chart.type === 'line' || chart.type === 'area' 
          ? 'rgba(59, 130, 246, 0.1)'
          : [
              'rgba(59, 130, 246, 0.8)',
              'rgba(16, 185, 129, 0.8)',
              'rgba(245, 158, 11, 0.8)',
              'rgba(239, 68, 68, 0.8)',
              'rgba(139, 92, 246, 0.8)',
              'rgba(236, 72, 153, 0.8)',
            ],
        borderColor: chart.type === 'line' || chart.type === 'area'
          ? 'rgba(59, 130, 246, 1)'
          : 'rgba(255, 255, 255, 1)',
        borderWidth: chart.type === 'line' || chart.type === 'area' ? 2 : 1,
        fill: chart.type === 'area',
        tension: 0.4,
      }]
    };

    const options = {
      responsive: true,
      maintainAspectRatio: false,
      plugins: {
        legend: {
          display: true,
          position: 'top',
        },
        tooltip: {
          backgroundColor: 'rgba(0, 0, 0, 0.8)',
          padding: 12,
          titleColor: '#fff',
          bodyColor: '#fff',
          borderColor: 'rgba(255, 255, 255, 0.1)',
          borderWidth: 1,
        }
      },
      scales: chart.type === 'pie' || chart.type === 'doughnut' || chart.type === 'radar' ? {} : {
        y: {
          beginAtZero: true,
          grid: {
            color: 'rgba(0, 0, 0, 0.05)',
          }
        },
        x: {
          grid: {
            display: false,
          }
        }
      }
    };

    switch (chart.type?.toLowerCase()) {
      case 'bar':
        return <Bar data={chartData} options={options} />;
      case 'line':
      case 'area':
        return <Line data={chartData} options={options} />;
      case 'pie':
        return <Pie data={chartData} options={options} />;
      case 'doughnut':
      case 'donut':
        return <Doughnut data={chartData} options={options} />;
      case 'radar':
        return <Radar data={chartData} options={options} />;
      default:
        return <div>Desteklenmeyen grafik tipi: {chart.type}</div>;
    }
  };

  const getStatusIcon = (status) => {
    switch (status) {
      case 'processing':
        return <span className="status-icon processing">✓</span>; // Mavi tek tik
      case 'completed':
        return <span className="status-icon completed">✓✓</span>; // Mavi çift tik
      case 'failed':
        return <IconAlertCircle className="status-icon failed" />;
      default:
        return <span className="status-icon pending">✓</span>; // Gri tek tik
    }
  };

  const getStatusText = (status) => {
    switch (status) {
      case 'processing':
        return 'İşleniyor';
      case 'completed':
        return 'Tamamlandı';
      case 'failed':
        return 'Başarısız';
      default:
        return 'Bekliyor';
    }
  };

  return (
    <>
      <div className="dashboard-page">
        {/* API Status Banner */}
        <div className={`api-status-banner ${apiStatus.online ? 'online' : 'offline'}`}>
        <div className="status-indicator">
          {apiStatus.checking ? (
            <><IconLoader2 className="spin" size={16} /> API Durumu Kontrol Ediliyor...</>
          ) : apiStatus.online ? (
            <><IconCheck size={16} /> Data Analysis API Aktif</>
          ) : (
            <><IconAlertCircle size={16} /> API Bağlantısı Yok (http://localhost:5221)</>
          )}
        </div>
        <span className="status-time">Son kontrol: {new Date().toLocaleTimeString('tr-TR')}</span>
      </div>

      {/* Aktif Analizler - İşleniyor */}
      {activeAnalyses.length > 0 && (
        <div className="active-analyses-section">
          <h2 className="section-title">
            <IconLoader2 className="spin" size={24} />
            Devam Eden Analizler
          </h2>
          <div className="active-analyses-grid">
            {activeAnalyses.map(analysis => (
              <div key={analysis.requestId} className="active-analysis-card">
                <div className="active-analysis-header">
                  <div className="database-icon">💾</div>
                  <div className="analysis-info">
                    <h3>{analysis.database}</h3>
                    <p className="analysis-meta">{analysis.tables?.length || 0} tablo</p>
                  </div>
                </div>
                <div className="progress-section">
                  <div className="progress-bar-container">
                    <div className="progress-bar" style={{ width: `${analysis.progress}%` }}></div>
                  </div>
                  <div className="progress-info">
                    <span className="progress-percentage">{analysis.progress}%</span>
                    <span className="progress-message">{analysis.message}</span>
                  </div>
                </div>
                <div className="analysis-details">
                  <div className="detail-item">
                    <IconClock size={16} />
                    <span>{formatDate(analysis.startedAt)}</span>
                  </div>
                  <div className="detail-item">
                    <span className="badge badge-info">{analysis.status === 'processing' ? 'İşleniyor' : 'Bekliyor'}</span>
                  </div>
                </div>
              </div>
            ))}
          </div>
        </div>
      )}

      {/* Tamamlanmış Analizler */}
      {completedAnalyses.length > 0 ? (
        <>
          {/* Database Tabs - En Üstte */}
          <div className="database-tabs-container">
            <div className="database-tabs">
              {completedAnalyses.map(analysis => (
                <button
                  key={analysis.requestId}
                  className={`database-tab ${activeTab === analysis.requestId ? 'active' : ''}`}
                  onClick={() => {
                    setActiveTab(analysis.requestId);
                    setCurrentReport(null);
                    setSelectedAnalysis(null);
                  }}
                >
                  <div className="tab-icon">💾</div>
                  <div className="tab-info">
                    <div className="tab-name">{analysis.database}</div>
                    <div className="tab-meta">{analysis.tables?.length || 0} tablo</div>
                  </div>
                  {getStatusIcon(analysis.status)}
                </button>
              ))}
            </div>
          </div>

          {/* Dashboard Layout */}
          <div className="dashboard-layout">
            {/* Sol Panel - Kontroller */}
            <div className="left-sidebar">
              {/* Aktif Tab İçeriği */}
              {activeTab && completedAnalyses.find(a => a.requestId === activeTab) && (
              <div className="analyses-list">
                {(() => {
                  const analysis = completedAnalyses.find(a => a.requestId === activeTab);
                  return (
                    <div key={analysis.requestId} className="completed-analysis-card">
                      <div className="completed-header">
                        {getStatusIcon(analysis.status)}
                        <div className="completed-info">
                          <h3>{analysis.database}</h3>
                          <p className="completed-meta">
                            {analysis.tables?.length || 0} tablo • 
                            Tamamlandı: {formatDate(analysis.completedAt || analysis.updatedAt)}
                          </p>
                        </div>
                      </div>

                      {/* AI Önerilen Raporlar */}
                      <div className="suggested-reports">
                        <h4>🤖 AI Önerilen Raporlar</h4>
                        <p className="report-hint">Yapay zeka tablolarınızı analiz etti. Tıklayın ve grafikleri görün:</p>
                        <div className="report-suggestions">
                          <button className="suggestion-btn" onClick={() => handleGenerateReport(analysis, 'user-activity')}>
                            📊 Kullanıcı Aktivite Raporu
                          </button>
                          <button className="suggestion-btn" onClick={() => handleGenerateReport(analysis, 'data-distribution')}>
                            📈 Veri Dağılım Analizi
                          </button>
                          <button className="suggestion-btn" onClick={() => handleGenerateReport(analysis, 'trend-analysis')}>
                            📉 Trend Analizi
                          </button>
                          <button className="suggestion-btn" onClick={() => handleGenerateReport(analysis, 'summary-statistics')}>
                            🔢 Özet İstatistikler
                          </button>
                        </div>
                      </div>

                      {/* Serbest Soru Sorma Alanı - CHAT INTERFACE */}
                      <div className="chat-section">
                        <h4>💬 AI Asistan</h4>
                        
                        {/* Chat Messages */}
                        <div className="chat-messages-container">
                          {messages.length === 0 ? (
                            <div className="chat-welcome">
                              <IconRobot size={48} />
                              <p>Merhaba! Verileriniz hakkında soru sorabilirsiniz.</p>
                              <div className="example-questions">
                                <small>Örnek sorular:</small>
                                <span onClick={() => handleQuickQuestion(analysis, 'En aktif kullanıcılar kimler?')}>
                                  En aktif kullanıcılar kimler?
                                </span>
                                <span onClick={() => handleQuickQuestion(analysis, 'Aylık veri artışı nedir?')}>
                                  Aylık veri artışı nedir?
                                </span>
                                <span onClick={() => handleQuickQuestion(analysis, 'Hangi tabloda en çok kayıt var?')}>
                                  Hangi tabloda en çok kayıt var?
                                </span>
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
                                  {msg.result && msg.result.charts && (
                                    <div className="message-meta">
                                      <IconChartBar size={14} />
                                      <span>{msg.result.charts.length} grafik</span>
                                    </div>
                                  )}
                                </div>
                              </div>
                            ))
                          )}
                          <div ref={chatEndRef} />
                        </div>

                        {/* Chat Input */}
                        <div className="chat-input-area">
                          <input
                            ref={questionInputRef}
                            type="text"
                            placeholder={`"${analysis.database}" hakkında soru sorun...`}
                            className="chat-input"
                            value={question}
                            onChange={(e) => setQuestion(e.target.value)}
                            onKeyPress={(e) => {
                              if (e.key === 'Enter' && !isQuerying) {
                                handleAskQuestion(analysis, question);
                              }
                            }}
                            disabled={isQuerying}
                          />
                          <button 
                            className="btn-send-chat"
                            onClick={() => handleAskQuestion(analysis, question)}
                            disabled={!question.trim() || isQuerying}
                          >
                            {isQuerying ? (
                              <IconLoader2 className="spin" size={20} />
                            ) : (
                              <IconSend size={20} />
                            )}
                          </button>
                        </div>
                      </div>
                    </div>
                  );
                })()}
                </div>
              )}
            </div> {/* Close left-sidebar */}
            
            {/* Sağ Panel - Aktif Rapor Görünümü */}
            <div className="right-panel">
            {currentReport ? (
              <div className="report-display">
                <div className="report-header">
                  <div>
                    <h2>{currentReport.title}</h2>
                    <p className="report-description">{currentReport.description}</p>
                    {activeReportType && <span className="report-type-badge">{activeReportType}</span>}
                  </div>
                  <button 
                    className="btn-close-report"
                    onClick={() => {
                      setCurrentReport(null);
                      setSelectedAnalysis(null);
                      setActiveReportType(null);
                    }}
                  >
                    ✕ Kapat
                  </button>
                </div>

                {/* Grafikler */}
                {currentReport.charts && currentReport.charts.length > 0 && (
                  <div className="charts-grid">
                    {currentReport.charts.map((chart, index) => (
                      <div key={index} className="chart-container">
                        <h3>{chart.title}</h3>
                        {renderChart(chart, index)}
                      </div>
                    ))}
                  </div>
                )}

                {/* Insights */}
                {currentReport.insights && currentReport.insights.length > 0 && (
                  <div className="insights-section">
                    <h3>💡 Önemli Bulgular</h3>
                    <div className="insights-grid">
                      {currentReport.insights.map((insight, index) => (
                        <div key={index} className={`insight-card insight-${insight.type}`}>
                          <div className="insight-icon">
                            {insight.type === 'success' && '✓'}
                            {insight.type === 'warning' && '⚠'}
                            {insight.type === 'info' && 'ℹ'}
                            {insight.type === 'error' && '✕'}
                          </div>
                          <div className="insight-content">
                            <h4>{insight.title}</h4>
                            <p>{insight.description}</p>
                          </div>
                        </div>
                      ))}
                    </div>
                  </div>
                )}

                {/* AI Cevabı (soru-cevap modunda) */}
                {currentReport.answer && (
                  <div className="ai-answer-section">
                    <h3>🤖 AI Cevabı</h3>
                    <div className="ai-answer-box">
                      {currentReport.answer}
                    </div>
                  </div>
                )}
              </div>
            ) : (
              <div className="no-report-placeholder">
                <div className="placeholder-content">
                  <IconChartBar size={64} />
                  <h3>Rapor Seçilmedi</h3>
                  <p>Sol menüden bir rapor seçin veya soru sorun</p>
                  {activeTab && (
                    <div className="placeholder-info">
                      <p className="info-text">
                        📊 Aktif Veritabanı: <strong>{completedAnalyses.find(a => a.requestId === activeTab)?.database}</strong>
                      </p>
                      <p className="info-text">
                        📋 Tablo Sayısı: <strong>{completedAnalyses.find(a => a.requestId === activeTab)?.tables?.length || 0}</strong>
                      </p>
                    </div>
                  )}
                </div>
              </div>
            )}
          </div>
        </div>
      </>
      ) : (
        <div className="no-data-placeholder">
          <div className="placeholder-content">
            <IconAlertCircle size={64} />
            <h3>Henüz Tamamlanmış Analiz Yok</h3>
            <p>Yeni bir analiz başlatmak için <strong>Veri Analizi</strong> sayfasına gidin</p>
            <button className="btn-primary" onClick={() => window.location.href = '/data-analysis'}>
              🔍 Yeni Analiz Başlat
            </button>
          </div>
        </div>
      )}
    </div>
    </>
  );
};

export default DashboardPage;
