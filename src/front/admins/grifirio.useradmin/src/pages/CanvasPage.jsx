import React, { useState, useEffect, useRef } from 'react';
import { useNavigate, useLocation } from 'react-router-dom';
import { IconArrowLeft, IconDatabase, IconRobot, IconUser, IconSend, IconLoader2, IconX, IconChevronLeft, IconChevronRight } from '@tabler/icons-react';
import InfiniteCanvas from '../components/Canvas/InfiniteCanvas';
import {
  getConnectionById, getSelectedTables,
  submitAgentQuery, getAgentQueryStatus, getAgentQueryResult,
} from '../services/dataAnalysisService';
import './CanvasPage.css';

/* ─────────────────────────────────────────────────────────────
   Canvas-node builder helpers
   Render tamamen sonuç odaklıdır: backend'in döndürdüğü composite
   payload'daki answer + charts[] + insights[] neyse o çizilir.
───────────────────────────────────────────────────────────── */
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
    newEdges.push({ id: `e-${parentId}-${tgtId}-${Date.now()}`, source: parentId, target: tgtId, animated: true, style: { stroke: 'var(--accent)' } });
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

/**
 * Soruyu ajan hattina gonderir ve sonucu bekler.
 *
 * Hat asenkron: gonder -> kuyruga girer -> durum sorulur -> sonuc alinir.
 * Cagiran taraf tek bir Promise gorsun diye yoklama burada kapsulleniyor.
 */
const AGENT_POLL_MS = 3000;
const AGENT_MAX_ATTEMPTS = 100; // ~5 dakika

const askViaAgent = async (question, { connectionId, onProgress }) => {
  if (!connectionId) {
    return { success: false, error: 'Bu kanvas bir bağlantıya bağlı değil.' };
  }

  // Sunucu 400 dondugunde sebebi govdede yaziyor ("Önce tabloları seçip Ön
  // Analiz çalıştırın" gibi). Axios bunu exception'a cevirdigi icin, yakalanip
  // acilmazsa kullaniciya yalnizca "Request failed with status code 400"
  // gorunuyordu — yani sunucu sebebi biliyor, ekran soylemiyordu.
  let submitted;
  try {
    submitted = await submitAgentQuery(connectionId, question);
  } catch (err) {
    const body = err.response?.data;
    return {
      success: false,
      error: body?.error || body?.detail || body?.title || err.message,
      status: body?.status,
      // Sunucu soruyu cozemedigini soyluyor ve ne sormasi gerektigini
      // yaziyor. Bu bir hata degil, karsi soru — ekranda da oyle gorunmeli.
      needsClarification: Boolean(body?.needsClarification),
    };
  }

  if (!submitted?.success) {
    return { success: false, error: submitted?.error || 'Sorgu gönderilemedi.' };
  }

  const queryId = submitted.queryId;
  onProgress?.('Sorgu kuyruğa alındı…');

  for (let attempt = 0; attempt < AGENT_MAX_ATTEMPTS; attempt++) {
    await new Promise(r => setTimeout(r, AGENT_POLL_MS));

    const status = await getAgentQueryStatus(queryId);

    if (status.status === 'completed') {
      const payload = await getAgentQueryResult(queryId);
      const result = payload?.result ?? {};
      return {
        success: true,
        answer: result.summary || result.answer || 'Analiz tamamlandı.',
        charts: result.charts || [],
        failedTasks: [],
        // Denetim izi: hangi tablo/kolon secildi, hangi SQL calisti.
        audit: { ...(result.audit || {}), llmParameters: payload?.llmParameters },
      };
    }

    if (status.status === 'failed') {
      return { success: false, error: status.message || status.error || 'Analiz başarısız oldu.' };
    }

    onProgress?.('Analiz ediliyor…');
  }

  return { success: false, error: 'Zaman aşımı — analiz 5 dakikada tamamlanmadı.' };
};

/* ─────────────────────────────────────────────────────────────
   CanvasPage
───────────────────────────────────────────────────────────── */
export default function CanvasPage() {
  const navigate = useNavigate();
  const location = useLocation();

  const [analysis, setAnalysis] = useState(null);
  const [sidebarOpen, setSidebarOpen] = useState(true);

  // Chat state
  const [messages, setMessages] = useState([]);
  const [question, setQuestion] = useState('');
  const [queryCount, setQueryCount] = useState(0);
  const isQuerying = queryCount > 0; // sadece topbar yüklenme göstergesi — input'u bloklamaz
  const chatEndRef = useRef(null);
  const inputRef = useRef(null);

  // Canvas state
  const [canvasNodes, setCanvasNodes] = useState([]);
  const [canvasEdges, setCanvasEdges] = useState([]);
  const nextPosRef = useRef({ x: 80, y: 80 });
  const lastGroupIdRef = useRef(null);

  /* Kanvas bir bağlantıdan açılıyor: /canvas?connectionId=...
     Veritabanı adı ve seçili tablolar sunucudan okunuyor. Bağlantı
     bulunamazsa bu ayrıca işaretleniyor — önceki sürümde sohbet kutusu
     sessizce kilitli açılıyor, sebebi hiçbir yerde yazmıyordu. */
  const [analysisMissing, setAnalysisMissing] = useState(false);

  // Kanvas bir baglantidan aciliyor: /canvas?connectionId=...
  const connectionId = new URLSearchParams(location.search).get('connectionId');

  useEffect(() => {
    let cancelled = false;

    const loadFromConnection = async () => {
      try {
        const [connection, selection] = await Promise.all([
          getConnectionById(connectionId),
          getSelectedTables(connectionId).catch(() => ({ tables: [] })),
        ]);

        if (cancelled) return;

        const conn = connection?.connection ?? connection?.data ?? connection;
        setAnalysis({
          requestId: connectionId,
          connectionId,
          database: conn?.database || conn?.name || 'Veritabanı',
          tables: selection?.tables ?? [],
        });
        setAnalysisMissing(false);
      } catch {
        if (!cancelled) {
          setAnalysis(null);
          setAnalysisMissing(true);
        }
      }
    };

    if (connectionId) {
      loadFromConnection();
      return () => { cancelled = true; };
    }

    // connectionId yoksa acilacak bir sey yok. Eskiden burada localStorage'dan
    // bir analiz kaydi aranirdi; kayit yalnizca o tarayicida durdugu icin
    // baska bir makineden girildiginde kanvas bozulmus gibi aciliyordu.
    setAnalysis(null);
    setAnalysisMissing(true);
    return () => { cancelled = true; };
  }, [connectionId]);

  /* Auto-scroll chat */
  useEffect(() => {
    chatEndRef.current?.scrollIntoView({ behavior: 'smooth' });
  }, [messages]);

  /* ── Ask AI question ── */
  const handleAsk = async (q) => {
    const text = (q || question).trim();
    if (!text) return;

    const userMsg = { role: 'user', content: text, ts: Date.now() };
    setMessages(p => [...p, userMsg]);
    setQuestion('');
    if (inputRef.current) inputRef.current.style.height = 'auto';
    setQueryCount(c => c + 1);
    setMessages(p => [...p, { role: 'ai', content: '', loading: true, ts: Date.now() }]);

    // Build history from existing messages (exclude the loading placeholder we just added)
    // Not: ajan hatti su an sohbet gecmisini almiyor; her soru bagimsiz
    // degerlendiriliyor. "Onu su kolona gore ver" gibi takip sorulari bu
    // yuzden calismaz — hattin gecmis destegi eklenene kadar boyle.

    // ── Her soru için tuvale hemen soru + loading node ekle ──
    // (grafik gelip gelmeyeceğine keyword değil, backend'in sonucu karar verir)
    const qNodeId       = `q-${Date.now()}`;
    const loadingNodeId = `chart-loading-${Date.now()}`;
    const qPos          = { ...nextPosRef.current };

    const qNode = {
      id: qNodeId, type: 'biInsightNode',
      position: qPos,
      data: { type: 'question', title: '💬 Soru', description: text },
    };
    const loadingNode = {
      id: loadingNodeId, type: 'biChartNode',
      position: { x: qPos.x + 460, y: qPos.y },
      data: { loading: true, title: 'Analiz ediliyor…' },
    };
    const qEdge = lastGroupIdRef.current ? {
      id: `e-${lastGroupIdRef.current}-${qNodeId}`,
      source: lastGroupIdRef.current, target: qNodeId,
      animated: true, style: { stroke: 'var(--slate-400)' },
    } : null;
    const loadEdge = {
      id: `e-${qNodeId}-${loadingNodeId}`,
      source: qNodeId, target: loadingNodeId,
      animated: true, style: { stroke: 'var(--accent)' },
    };

    setCanvasNodes(p => [...p, qNode, loadingNode]);
    setCanvasEdges(p => [...p, ...(qEdge ? [qEdge] : []), loadEdge]);
    lastGroupIdRef.current = qNodeId;

    try {
      // Kanvas artik ajan hattini kullaniyor (/api/agent/query).
      //
      // Onceden Django/RabbitMQ hattina gidiyordu; o hat on analiz kapisini,
      // semantik sozlugu ve denetim izini tanimiyor. Yani kanvastan sorulan
      // soru, kolon adlarini ogrendigimiz butun altyapiyi atliyordu. Ayni
      // isi yapan iki paralel hat vardi; kanvas ekrani korunup alttaki
      // hat tekillestirildi.
      const res = await askViaAgent(text, {
        connectionId: analysis?.connectionId || analysis?.requestId,
        onProgress: (message) => {
          if (!message) return;
          setCanvasNodes(p => p.map(n =>
            n.id === loadingNodeId
              ? { ...n, data: { ...n.data, title: message } }
              : n
          ));
        },
      });

      if (res.success) {
        const answer = res.answer || '';
        setMessages(p => {
          const a = [...p];
          a[a.length - 1] = {
            role: 'ai', content: answer, ts: Date.now(),
            result: { charts: res.charts || [] },
            audit: res.audit || null,
          };
          return a;
        });

        // Loading node'u kaldır, tüm artifact'leri (answer + N grafik +
        // başarısız görev uyarıları) soru node'una bağlı olarak yerleştir
        setCanvasNodes(p => p.filter(n => n.id !== loadingNodeId));
        setCanvasEdges(p => p.filter(e => e.target !== loadingNodeId));

        const report = {
          answer,
          charts: res.charts || [],
          insights: (res.failedTasks || []).map(f => ({
            type: 'warning',
            title: `⚠ ${f.title || 'Görev tamamlanamadı'}`,
            description: f.reason || '',
          })),
        };

        nextPosRef.current = { x: qPos.x + 460, y: qPos.y };
        const { newNodes, newEdges } = buildCanvasNodes(report, qNodeId, nextPosRef);
        setCanvasNodes(p => [...p, ...newNodes]);
        setCanvasEdges(p => [...p, ...newEdges]);
        nextPosRef.current = { x: qPos.x, y: nextPosRef.current.y };
      } else {
        // Netleştirme, hatadan farklı: sistem çalıştı ama soruyu çözemedi ve
        // ne sorması gerektiğini biliyor. Kırmızı "Hata" göstermek kullanıcıya
        // yanlış bir şey yaptığını düşündürüyor — oysa yapılacak tek şey
        // soruyu biraz daha açık yazmak.
        const asksBack = res.needsClarification;
        const label = asksBack ? '💬 Bir sorum var' : '❌ Hata';
        const body = res.error || 'Hata oluştu';

        if (loadingNodeId) {
          setCanvasNodes(p => p.map(n =>
            n.id === loadingNodeId
              ? { ...n, type: 'biInsightNode', data: { type: asksBack ? 'warning' : 'error', title: label, description: body } }
              : n
          ));
          setCanvasEdges(p => p.map(e => e.target === loadingNodeId ? { ...e, animated: false } : e));
        }
        setMessages(p => {
          const a = [...p];
          a[a.length - 1] = {
            role: 'ai',
            content: asksBack ? body : `❌ ${body}`,
            error: !asksBack,
            ts: Date.now(),
            // On analiz tamamlanmadiysa kullaniciyi burada birakmayalim:
            // kanvastan cikis yolu olmadan "yapamazsin" demek, cikmaz sokak.
            needsPreAnalysis: Boolean(res.status) && res.status !== 'ready',
          };
          return a;
        });
      }
    } catch (e) {
      if (loadingNodeId) {
        setCanvasNodes(p => p.map(n =>
          n.id === loadingNodeId
            ? { ...n, type: 'biInsightNode', data: { type: 'error', title: '❌ Hata', description: e.message } }
            : n
        ));
        setCanvasEdges(p => p.map(e => e.target === loadingNodeId ? { ...e, animated: false } : e));
      }
      setMessages(p => {
        const a = [...p];
        a[a.length - 1] = { role: 'ai', content: `❌ ${e.message}`, error: true, ts: Date.now() };
        return a;
      });
    } finally {
      setQueryCount(c => Math.max(0, c - 1));
    }
  };

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
          {isQuerying && (
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

          {/* "AI Önerilen Raporlar" bölümü kaldırıldı. Arkasındaki uç gerçek
              bir rapor üretmiyordu: içinde "TODO: Django AI'ya rapor talebi
              gönder" duruyor ve her çağrı "Rapor Oluşturuluyor" başlıklı sahte
              bir grafik döndürüyordu. Butonlar zaten kapalıydı; hem uç hem
              butonlar gitti — çalışmayan bir bölüm, olmayan bir bölümden
              daha çok soru doğuruyor. */}

          <div className="cp-divider" />

          {/* Chat */}
          <div className="cp-section cp-chat-section">
            <div className="cp-section-title">
              <IconRobot size={14} /> AI Asistan
            </div>

            {analysisMissing && (
              <div className="cp-missing">
                <strong>Bu analiz kaydı bu tarayıcıda bulunamadı.</strong>
                <span>
                  Analiz kayıtları şu an yalnızca tarayıcıda saklanıyor; başka bir cihazdan
                  açtıysanız ya da tarayıcı verisi temizlendiyse görünmez. Dashboard’dan
                  analizi yeniden başlatarak devam edebilirsiniz.
                </span>
                <button className="cp-back" onClick={() => navigate('/')}>
                  Dashboard’a dön
                </button>
              </div>
            )}

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

                      {msg.needsPreAnalysis && (
                        <button
                          className="cp-quick-q"
                          style={{ marginTop: 8, width: '100%' }}
                          onClick={() => navigate('/settings/sql-connection')}
                        >
                          Ön Analiz’i tamamla →
                        </button>
                      )}

                      {/* Denetim: sonucun dogru olup olmadigini anlamanin tek
                          yolu, hangi karara varildigini gormek. Grafigin
                          makul gorunmesi dogrulugunu kanitlamiyor. */}
                      {msg.audit && (
                        <details className="cp-audit">
                          <summary>Nasıl hesaplandı?</summary>
                          <dl className="cp-audit-rows">
                            {msg.audit.targetTable && (
                              <div><dt>Tablo</dt><dd>{msg.audit.targetTable}</dd></div>
                            )}
                            {msg.audit.groupBy?.length > 0 && (
                              <div><dt>Gruplama</dt><dd>{msg.audit.groupBy.join(', ')}</dd></div>
                            )}
                            {msg.audit.targetColumn && (
                              <div><dt>Ölçüm</dt><dd>{msg.audit.targetColumn}</dd></div>
                            )}
                            {msg.audit.aggregation && (
                              <div><dt>İşlem</dt><dd>{msg.audit.aggregation}</dd></div>
                            )}
                            {msg.audit.filters && Object.keys(msg.audit.filters).length > 0 && (
                              <div>
                                <dt>Filtre</dt>
                                <dd>{Object.entries(msg.audit.filters).map(([k, v]) => `${k} = ${v}`).join(' · ')}</dd>
                              </div>
                            )}
                            {typeof msg.audit.rowsRead === 'number' && (
                              <div><dt>Okunan satır</dt><dd>{msg.audit.rowsRead}</dd></div>
                            )}
                          </dl>

                          {msg.audit.executedSql && (
                            <>
                              <div className="cp-audit-label">Çalıştırılan SQL</div>
                              <pre className="cp-audit-sql">{msg.audit.executedSql}</pre>
                            </>
                          )}

                          {msg.audit.aggregationPerformedIn === 'pandas' && (
                            <p className="cp-audit-warn">
                              ⚠ Gruplama ve toplama veritabanında değil, çekilen satırlar
                              üzerinde bellekte yapıldı. Tablo bu satır sayısından büyükse
                              sonuç <strong>kısmi veriye</strong> dayanır.
                            </p>
                          )}
                        </details>
                      )}
                    </div>
                  </div>
                ))
              )}
              <div ref={chatEndRef} />
            </div>

            {/* Input */}
            <div className="cp-input-row">
              <textarea
                ref={inputRef}
                rows={1}
                placeholder={analysis ? `"${analysis.database}" hakkında sor…` : 'Yükleniyor…'}
                value={question}
                onChange={e => {
                  setQuestion(e.target.value);
                  e.target.style.height = 'auto';
                  e.target.style.height = Math.min(e.target.scrollHeight, 120) + 'px';
                }}
                onKeyDown={e => {
                  if (e.key === 'Enter' && !e.shiftKey) {
                    e.preventDefault();
                    if (!question.trim() || !analysis) return;
                    handleAsk();
                  }
                }}
                disabled={!analysis}
                className="cp-input"
              />
              <button className="cp-send" onClick={() => handleAsk()} disabled={!question.trim() || !analysis}>
                <IconSend size={16} />
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
