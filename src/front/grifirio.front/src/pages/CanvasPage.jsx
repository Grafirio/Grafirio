import React, { useState, useEffect, useRef, useCallback } from 'react';
import { useParams, useNavigate } from 'react-router-dom';
import { IconArrowLeft, IconDatabase, IconRobot, IconUser, IconSend, IconLoader2, IconX, IconChevronLeft, IconChevronRight } from '@tabler/icons-react';
import InfiniteCanvas from '../components/Canvas/InfiniteCanvas';
import { generateAIReport } from '../services/dataAnalysisService';
import { sendChatMessage } from '../services/geminiChatService';
import './CanvasPage.css';

/* ─────────────────────────────────────────────────────────────
   Canvas-node builder helpers
───────────────────────────────────────────────────────────── */
const CHART_KEYWORDS = [
  // ── Genel tetikleyiciler ──────────────────────────────────────────────
  'grafik', 'chart', 'görselleştir', 'gorselleştir', 'görsel olarak', 'diyagram', 'diagram',
  'grafikle', 'grafiğini', 'grafiği', 'göster', 'goster', 'çiz', 'ciz', 'oluştur',
  'grafik olarak', 'olarak göster', 'olarak goster', 'grafigini', 'grafigi', 'grafik olustur',

  // ── Bar / Sütun / Kolon ───────────────────────────────────────────────
  'bar', 'bar chart', 'bar grafik', 'bar grafiği',
  'çubuk', 'cubuk', 'çubuk grafik', 'çubuk grafiği',
  'sütun', 'sutun', 'sütun grafiği', 'sütun grafik',
  'kolon', 'kolon grafik',
  'dikey grafik', 'yatay grafik',
  'grouped bar', 'stacked bar', 'yığılmış çubuk', 'yığılmış bar',
  'karşılaştırmalı grafik', 'karşılaştır',

  // ── Çizgi / Alan / Trend ─────────────────────────────────────────────
  'çizgi', 'cizgi', 'çizgi grafik', 'çizgi grafiği', 'line', 'line chart',
  'trend', 'trend grafiği', 'trend analizi',
  'zaman serisi', 'time series', 'zaman grafik',
  'alan', 'alan grafiği', 'area', 'area chart',
  'yığılmış alan', 'stacked area',
  'step chart', 'adım grafik',
  'spline', 'eğri grafik',

  // ── Pasta / Halka / Dilim ─────────────────────────────────────────────
  'pasta', 'pasta grafik', 'pasta grafiği', 'pie', 'pie chart', 'pie grafik',
  'halka', 'halka grafik', 'halka grafiği', 'doughnut', 'donut',
  'dilim', 'oran grafiği', 'yüzde grafiği', 'yüzde dağılımı',
  'oranlar', 'yüzde',

  // ── Dağılım / Nokta ───────────────────────────────────────────────────
  'dağılım', 'dagılım', 'dağılım grafiği', 'scatter', 'scatter plot',
  'nokta grafik', 'nokta grafiği', 'bubble', 'balon', 'balon grafik',
  'korelasyon', 'correlation',

  // ── Histogram / Frekans ───────────────────────────────────────────────
  'histogram', 'frekans grafiği', 'frekans dağılımı', 'dağılım histogramı',

  // ── Radar / Örümcek ───────────────────────────────────────────────────
  'radar', 'radar grafik', 'spider', 'örümcek ağı', 'örümcek grafik',
  'polar', 'polar grafik',

  // ── Isı Haritası ─────────────────────────────────────────────────────
  'ısı haritası', 'isi haritası', 'heatmap', 'heat map',

  // ── Kutu / İstatistik ─────────────────────────────────────────────────
  'kutu grafik', 'kutu grafiği', 'box plot', 'box-whisker', 'whisker',
  'violin plot', 'keman grafik',

  // ── Huni / Satış Hunisi ───────────────────────────────────────────────
  'huni', 'huni grafik', 'huni grafiği', 'funnel', 'funnel chart',
  'satış hunisi', 'dönüşüm hunisi',

  // ── Şelale / Waterfall ────────────────────────────────────────────────
  'şelale', 'şelale grafik', 'şelale grafiği', 'waterfall', 'waterfall chart',
  'kümülatif grafik', 'kümülatif',

  // ── Ağaç Haritası / Treemap ───────────────────────────────────────────
  'treemap', 'ağaç haritası', 'agac haritası', 'hiyerarşik grafik',

  // ── Pareto ────────────────────────────────────────────────────────────
  'pareto', 'pareto grafik', 'pareto analizi',

  // ── Gantt / Zaman Çizelgesi ───────────────────────────────────────────
  'gantt', 'gantt grafik', 'zaman çizelgesi', 'timeline',

  // ── Finansal ─────────────────────────────────────────────────────────
  'mum grafik', 'mum grafiği', 'candlestick', 'ohlc',

  // ── İstatistik Genel ─────────────────────────────────────────────────
  'istatistik', 'istatistiksel', 'analiz grafik', 'dağılımı göster',
  'görselle', 'tablo grafik',
];

const isChartRequest = (text) => {
  const lower = text.toLowerCase();
  return CHART_KEYWORDS.some(kw => lower.includes(kw));
};
const buildCanvasNodes = (report, parentId, posRef) => {
  const newNodes = [];
  const newEdges = [];
  const { x: sx, y: sy } = posRef.current;
  const COL_W = 430;
  const ROW_H_CHART = 330;
  const ROW_H_INSIGHT = 200;
  let curY = sy;

  const edge = (tgtId) => {
    if (!parentId) return;
    newEdges.push({ id: `e-${parentId}-${tgtId}-${Date.now()}`, source: parentId, target: tgtId, animated: true, style: { stroke: '#7c3aed' } });
  };

  if (report.answer) {
    const id = `ins-ans-${Date.now()}`;
    newNodes.push({ id, type: 'biInsightNode', position: { x: sx, y: curY }, data: { type: 'info', title: '🤖 AI Yanıtı', description: report.answer } });
    edge(id);
    curY += ROW_H_INSIGHT + 24;
  }

  (report.charts || []).forEach((chart, i) => {
    const id = `chart-${Date.now()}-${i}`;
    newNodes.push({ id, type: 'biChartNode', position: { x: sx + (i % 2) * COL_W, y: curY + Math.floor(i / 2) * (ROW_H_CHART + 32) }, data: chart });
    edge(id);
  });
  if (report.charts?.length) curY += Math.ceil(report.charts.length / 2) * (ROW_H_CHART + 32);

  (report.insights || []).forEach((insight, i) => {
    const id = `ins-${Date.now()}-${i}`;
    newNodes.push({ id, type: 'biInsightNode', position: { x: sx + (i % 2) * COL_W, y: curY + Math.floor(i / 2) * (ROW_H_INSIGHT + 20) }, data: insight });
    edge(id);
  });
  if (report.insights?.length) curY += Math.ceil(report.insights.length / 2) * (ROW_H_INSIGHT + 20);

  posRef.current = { x: sx, y: Math.max(sy, curY) + 60 };
  return { newNodes, newEdges };
};

/* ─────────────────────────────────────────────────────────────
   CanvasPage
───────────────────────────────────────────────────────────── */
export default function CanvasPage() {
  const { analysisId } = useParams();
  const navigate = useNavigate();

  const [analysis, setAnalysis] = useState(null);
  const [sidebarOpen, setSidebarOpen] = useState(true);

  // Chat state
  const [messages, setMessages] = useState([]);
  const [question, setQuestion] = useState('');
  const [isQuerying, setIsQuerying] = useState(false);
  const [loadingReport, setLoadingReport] = useState(false);
  const chatEndRef = useRef(null);
  const inputRef = useRef(null);

  // Canvas state
  const [canvasNodes, setCanvasNodes] = useState([]);
  const [canvasEdges, setCanvasEdges] = useState([]);
  const nextPosRef = useRef({ x: 80, y: 80 });
  const lastGroupIdRef = useRef(null);

  /* Load analysis from localStorage */
  useEffect(() => {
    const stored = localStorage.getItem('activeAnalyses');
    if (stored) {
      const all = JSON.parse(stored);
      const found = all.find(a => a.requestId === analysisId);
      if (found) setAnalysis(found);
    }
  }, [analysisId]);

  /* Auto-scroll chat */
  useEffect(() => {
    chatEndRef.current?.scrollIntoView({ behavior: 'smooth' });
  }, [messages]);

  /* ── Add report to canvas ── */
  const addToCanvas = useCallback((report, questionText) => {
    if (!report) return;

    const qId = questionText ? `q-${Date.now()}` : null;

    if (qId) {
      const qPos = { ...nextPosRef.current };
      const qNode = { id: qId, type: 'biInsightNode', position: qPos, data: { type: 'question', title: '💬 Soru', description: questionText } };
      nextPosRef.current = { x: qPos.x + 450, y: qPos.y };
      const qEdge = lastGroupIdRef.current ? { id: `e-${lastGroupIdRef.current}-${qId}`, source: lastGroupIdRef.current, target: qId, animated: true, style: { stroke: '#a78bfa' } } : null;
      const { newNodes, newEdges } = buildCanvasNodes(report, qId, nextPosRef);
      setCanvasNodes(p => [...p, qNode, ...newNodes]);
      setCanvasEdges(p => [...p, ...(qEdge ? [qEdge] : []), ...newEdges]);
      lastGroupIdRef.current = qId;
    } else {
      const { newNodes, newEdges } = buildCanvasNodes(report, lastGroupIdRef.current, nextPosRef);
      setCanvasNodes(p => [...p, ...newNodes]);
      setCanvasEdges(p => [...p, ...newEdges]);
      if (newNodes.length > 0) lastGroupIdRef.current = newNodes[0].id;
    }
  }, []);

  /* ── Generate preset report ── */
  const handleReport = async (reportType) => {
    if (!analysis) return;
    setLoadingReport(true);
    try {
      const res = await generateAIReport(analysis.requestId, reportType, analysis.database, analysis.tables || []);
      if (res.success) addToCanvas(res, null);
    } catch (e) {
      console.error(e);
    } finally {
      setLoadingReport(false);
    }
  };

  /* ── Ask AI question (Gemini via Django AI service) ── */
  const handleAsk = async (q) => {
    const text = (q || question).trim();
    if (!text) return;

    const userMsg = { role: 'user', content: text, ts: Date.now() };
    setMessages(p => [...p, userMsg]);
    setQuestion('');
    setIsQuerying(true);
    setMessages(p => [...p, { role: 'ai', content: '', loading: true, ts: Date.now() }]);

    // Build Gemini history from existing messages (exclude the loading placeholder we just added)
    const history = messages.map(m => ({
      role: m.role === 'user' ? 'user' : 'model',
      content: m.content,
    })).filter(m => m.content);

    // ── Grafik isteği ise tuval'e hemen loading node ekle ──
    const chartReq = isChartRequest(text);
    let loadingNodeId = null;

    if (chartReq) {
      const qNodeId       = `q-${Date.now()}`;
      loadingNodeId       = `chart-loading-${Date.now()}`;
      const qPos          = { ...nextPosRef.current };

      const qNode = {
        id: qNodeId, type: 'biInsightNode',
        position: qPos,
        data: { type: 'question', title: '💬 Soru', description: text },
      };
      const loadingNode = {
        id: loadingNodeId, type: 'biChartNode',
        position: { x: qPos.x + 460, y: qPos.y },
        data: { loading: true, title: 'Grafik Hazırlanıyor...' },
      };
      const qEdge = lastGroupIdRef.current ? {
        id: `e-${lastGroupIdRef.current}-${qNodeId}`,
        source: lastGroupIdRef.current, target: qNodeId,
        animated: true, style: { stroke: '#a78bfa' },
      } : null;
      const loadEdge = {
        id: `e-${qNodeId}-${loadingNodeId}`,
        source: qNodeId, target: loadingNodeId,
        animated: true, style: { stroke: '#7c3aed' },
      };

      setCanvasNodes(p => [...p, qNode, loadingNode]);
      setCanvasEdges(p => [...p, ...(qEdge ? [qEdge] : []), loadEdge]);
      nextPosRef.current    = { x: qPos.x, y: qPos.y + 360 };
      lastGroupIdRef.current = qNodeId;
    }

    try {
      const res = await sendChatMessage(text, history);

      if (res.success) {
        const answer = res.answer || '';
        setMessages(p => {
          const a = [...p];
          a[a.length - 1] = { role: 'ai', content: answer, ts: Date.now() };
          return a;
        });

        // Yanıt tipine göre karar ver — keyword'e değil, gerçek sonuca bak
        const isChartResponse = res.type === 'chart' && (res.charts?.length ?? 0) > 0;

        if (chartReq && loadingNodeId) {
          // Loading node zaten tuvale eklendi — sonuca göre güncelle
          if (isChartResponse) {
            setCanvasNodes(p => p.map(n =>
              n.id === loadingNodeId
                ? { ...n, data: { ...res.charts[0], loading: false } }
                : n
            ));
          } else {
            // Grafik bekleniyordu ama metin geldi — insight'a dönüştür
            setCanvasNodes(p => p.map(n =>
              n.id === loadingNodeId
                ? { ...n, type: 'biInsightNode', data: { type: 'info', title: '🤖 AI Yanıtı', description: answer } }
                : n
            ));
          }
          setCanvasEdges(p => p.map(e =>
            e.target === loadingNodeId ? { ...e, animated: false } : e
          ));
        } else if (isChartResponse) {
          // Grafik keyword'ü yoktu ama yanıt grafik — loading node olmadan chart node ekle
          const qNodeId2      = `q-${Date.now()}`;
          const chartNodeId2  = `chart-${Date.now()}-0`;
          const qPos2         = { ...nextPosRef.current };
          const qNode2 = {
            id: qNodeId2, type: 'biInsightNode',
            position: qPos2,
            data: { type: 'question', title: '💬 Soru', description: text },
          };
          const chartNode2 = {
            id: chartNodeId2, type: 'biChartNode',
            position: { x: qPos2.x + 460, y: qPos2.y },
            data: { ...res.charts[0], loading: false },
          };
          const qEdge2 = lastGroupIdRef.current ? {
            id: `e-${lastGroupIdRef.current}-${qNodeId2}`,
            source: lastGroupIdRef.current, target: qNodeId2,
            animated: false, style: { stroke: '#a78bfa' },
          } : null;
          const chartEdge2 = {
            id: `e-${qNodeId2}-${chartNodeId2}`,
            source: qNodeId2, target: chartNodeId2,
            animated: false, style: { stroke: '#7c3aed' },
          };
          setCanvasNodes(p => [...p, qNode2, chartNode2]);
          setCanvasEdges(p => [...p, ...(qEdge2 ? [qEdge2] : []), chartEdge2]);
          nextPosRef.current     = { x: qPos2.x, y: qPos2.y + 360 };
          lastGroupIdRef.current = qNodeId2;
        } else {
          // Saf metin yanıtı
          addToCanvas({ answer, charts: [], insights: [] }, text);
        }
      } else {
        // Hata — varsa loading node'u kaldır
        if (loadingNodeId) {
          setCanvasNodes(p => p.filter(n => n.id !== loadingNodeId));
          setCanvasEdges(p => p.filter(e => e.target !== loadingNodeId));
        }
        setMessages(p => {
          const a = [...p];
          a[a.length - 1] = { role: 'ai', content: `❌ ${res.error || 'Hata oluştu'}`, error: true, ts: Date.now() };
          return a;
        });
      }
    } catch (e) {
      if (loadingNodeId) {
        setCanvasNodes(p => p.filter(n => n.id !== loadingNodeId));
        setCanvasEdges(p => p.filter(e => e.target !== loadingNodeId));
      }
      setMessages(p => {
        const a = [...p];
        a[a.length - 1] = { role: 'ai', content: `❌ ${e.message}`, error: true, ts: Date.now() };
        return a;
      });
    } finally {
      setIsQuerying(false);
    }
  };

  const PRESETS = [
    { key: 'user-activity',      label: 'Kullanıcı Aktivitesi', icon: '📊' },
    { key: 'data-distribution',  label: 'Veri Dağılımı',        icon: '📈' },
    { key: 'trend-analysis',     label: 'Trend Analizi',        icon: '📉' },
    { key: 'summary-statistics', label: 'Özet İstatistikler',   icon: '🔢' },
  ];

  const QUICK_Q = ['En aktif kullanıcılar?', 'Aylık veri artışı?', 'En büyük tablo?'];

  return (
    <div className="cp-root">

      {/* ══ TOP BAR ══════════════════════════════════════════ */}
      <div className="cp-topbar">
        <button className="cp-back" onClick={() => navigate('/')}>
          <IconArrowLeft size={16} /> Geri
        </button>

        <div className="cp-breadcrumb">
          <IconDatabase size={14} />
          <span className="cp-db-name">{analysis?.database ?? '—'}</span>
          <span className="cp-bc-sep">/</span>
          <span className="cp-bc-page">Analiz Tuvali</span>
        </div>

        <div className="cp-topbar-info">
          {analysis && (
            <span className="cp-topbar-badge">
              {analysis.tables?.length ?? 0} tablo
            </span>
          )}
          {(loadingReport || isQuerying) && (
            <span className="cp-topbar-loading">
              <IconLoader2 size={14} className="spin" /> Analiz ediliyor…
            </span>
          )}
        </div>

        <button className="cp-sidebar-toggle" onClick={() => setSidebarOpen(o => !o)} title={sidebarOpen ? 'Paneli Kapat' : 'Paneli Aç'}>
          {sidebarOpen ? <IconChevronLeft size={16} /> : <IconChevronRight size={16} />}
        </button>
      </div>

      {/* ══ MAIN AREA ════════════════════════════════════════ */}
      <div className="cp-body">

        {/* ── Sol Panel: LLM Chat ── */}
        <aside className={`cp-sidebar ${sidebarOpen ? 'cp-sidebar--open' : 'cp-sidebar--closed'}`}>

          {/* Preset raporlar */}
          <div className="cp-section">
            <div className="cp-section-title">AI Önerilen Raporlar</div>
            <div className="cp-presets">
              {PRESETS.map(p => (
                <button key={p.key} className="cp-preset-btn" onClick={() => handleReport(p.key)} disabled={loadingReport || !analysis}>
                  <span>{p.icon}</span> {p.label}
                </button>
              ))}
            </div>
          </div>

          <div className="cp-divider" />

          {/* Chat */}
          <div className="cp-section cp-chat-section">
            <div className="cp-section-title">
              <IconRobot size={14} /> AI Asistan
            </div>

            <div className="cp-messages">
              {messages.length === 0 ? (
                <div className="cp-welcome">
                  <IconRobot size={40} strokeWidth={1.2} />
                  <p>Verileriniz hakkında soru sorun.<br />Sonuçlar tuvale eklenir.</p>
                  <div className="cp-quick-qs">
                    {QUICK_Q.map(q => (
                      <button key={q} className="cp-quick-q" onClick={() => handleAsk(q)}>{q}</button>
                    ))}
                  </div>
                </div>
              ) : (
                messages.map((msg, i) => (
                  <div key={i} className={`cp-msg cp-msg--${msg.role}${msg.error ? ' cp-msg--error' : ''}`}>
                    <div className="cp-msg-avatar">
                      {msg.role === 'user' ? <IconUser size={14} /> : <IconRobot size={14} />}
                    </div>
                    <div className="cp-msg-body">
                      {msg.loading ? (
                        <div className="cp-typing"><span/><span/><span/></div>
                      ) : (
                        msg.content
                      )}
                      {msg.result?.charts?.length > 0 && (
                        <div className="cp-msg-meta">📊 {msg.result.charts.length} grafik tuvale eklendi</div>
                      )}
                    </div>
                  </div>
                ))
              )}
              <div ref={chatEndRef} />
            </div>

            {/* Input */}
            <div className="cp-input-row">
              <input
                ref={inputRef}
                type="text"
                placeholder={analysis ? `"${analysis.database}" hakkında sor…` : 'Yükleniyor…'}
                value={question}
                onChange={e => setQuestion(e.target.value)}
                onKeyDown={e => e.key === 'Enter' && !isQuerying && handleAsk()}
                disabled={isQuerying || !analysis}
                className="cp-input"
              />
              <button className="cp-send" onClick={() => handleAsk()} disabled={!question.trim() || isQuerying || !analysis}>
                {isQuerying ? <IconLoader2 size={16} className="spin" /> : <IconSend size={16} />}
              </button>
            </div>
          </div>
        </aside>

        {/* ── Sağ: Sonsuz Tuval ── */}
        <main className="cp-canvas-area">
          <InfiniteCanvas nodes={canvasNodes} edges={canvasEdges} />
        </main>
      </div>
    </div>
  );
}
