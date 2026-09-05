import React, { useState, useEffect, useRef, useCallback } from 'react';
import { useNavigate, useLocation } from 'react-router-dom';
import { IconArrowLeft, IconDatabase, IconLoader2 } from '@tabler/icons-react';
import InfiniteCanvas from '../components/Canvas/InfiniteCanvas';
import { loadLayout, saveLayout, applyLayout, emptyLayout } from '../components/Canvas/canvasLayout';
import { nodeIds } from '../components/Canvas/canvasGraph';
import { DEFAULT_CHART_TYPE } from '../components/Canvas/chartTypes';
import DeleteConfirmDialog from '../components/Canvas/DeleteConfirmDialog';
import useRelationshipConfirmations from '../hooks/useRelationshipConfirmations';
import restoreFromHistory from '../utils/restoreFromHistory';
import getPendingConfirmations from '../utils/relationships/getPendingConfirmations';
import askViaAgent from '../services/askViaAgent';
import {
  getConnectionSummary, getSelectedTables,
  getAgentQueryHistory,
} from '../services/dataAnalysisService';
import './CanvasPage.css';


/* ─────────────────────────────────────────────────────────────
   CanvasPage
───────────────────────────────────────────────────────────── */
export default function CanvasPage() {
  const navigate = useNavigate();
  const location = useLocation();

  const [analysis, setAnalysis] = useState(null);

  /* Sol paneldeki sohbet KALDIRILDI. Bütün kartların soruları tek akışta
     toplandığı için ayrı konuların cümleleri alt alta düşüyor ve hangi
     sorunun hangi grafiğe ait olduğu okunmuyordu. Konuşma artık kendi
     kartının içinde duruyor. */
  const [queryCount, setQueryCount] = useState(0);
  const isQuerying = queryCount > 0; // yalnızca üst çubuktaki gösterge

  // Canvas state
  const [canvasNodes, setCanvasNodes] = useState([]);

  // Silme onayı bekleyen düğüm
  const [pendingDelete, setPendingDelete] = useState(null);

  /* Kanvas bir bağlantıdan açılıyor: /canvas?connectionId=...
     Veritabanı adı ve seçili tablolar sunucudan okunuyor. Bağlantı
     bulunamazsa bu ayrıca işaretleniyor — önceki sürümde sohbet kutusu
     sessizce kilitli açılıyor, sebebi hiçbir yerde yazmıyordu. */
  const [analysisMissing, setAnalysisMissing] = useState(false);

  // Kanvas bir baglantidan aciliyor: /canvas?connectionId=...
  const connectionId = new URLSearchParams(location.search).get('connectionId');

  /* ── Yerleşim kaydı ──
     Konum, silme ve dal bilgisi burada; sunucu bunları bilmiyor.
     Yazma sıklığı sürükleme hızına bağlı olduğu için geciktiriliyor. */
  const layoutRef = useRef(emptyLayout());
  const saveTimerRef = useRef(null);

  const persistLayout = useCallback(() => {
    if (!connectionId) return;
    clearTimeout(saveTimerRef.current);
    saveTimerRef.current = setTimeout(
      () => saveLayout(connectionId, layoutRef.current), 250);
  }, [connectionId]);

  useEffect(() => () => clearTimeout(saveTimerRef.current), []);

  const rememberPositions = useCallback((nodes) => {
    for (const node of nodes) {
      layoutRef.current.positions[node.id] = { ...node.position };
    }
    persistLayout();
  }, [persistLayout]);

  useEffect(() => {
    let cancelled = false;

    const loadFromConnection = async () => {
      try {
        const [connection, selection] = await Promise.all([
          getConnectionSummary(connectionId),
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

  /* Geçmişi tuvale geri yükle.

     Kanvas durumu yalnızca React state'inde yaşıyordu: çıkıp giren, sayfayı
     yenileyen ya da başka bir sayfaya gidip dönen kullanıcı boş ekran
     görüyordu. Veri kaybolmuyordu — sorular, üretilen parametreler ve
     sonuçlar sunucuda `QueryHistory` içinde duruyordu — ama hiçbir yerden
     geri okunmuyordu.

     Sunucudan gelen tuval "ham" hâl; üzerine kullanıcının kendi yerleşimi
     (sürüklediği konumlar, sildikleri, tuval üzerinden açtığı dallar,
     düzelttiği grafikler) uygulanıyor. */
  useEffect(() => {
    if (!connectionId) return undefined;
    let cancelled = false;

    (async () => {
      const layout = loadLayout(connectionId);
      layoutRef.current = layout;

      try {
        const { queries = [], relationshipDecisions = {} } = await getAgentQueryHistory(connectionId);
        if (cancelled || queries.length === 0) return;

        const restored = restoreFromHistory(queries, relationshipDecisions);
        const laidOut = applyLayout(restored, layout);
        setCanvasNodes(laidOut.nodes);
      } catch {
        // Geçmiş okunamazsa kanvas boş açılır ve yeni soru sorulabilir.
        // Eski sohbeti gösterememek, ekranı tamamen kilitlemekten iyidir.
      }
    })();

    return () => { cancelled = true; };
  }, [connectionId]);

  /* ── Düğüm sürükleme ── */
  const handleNodeMove = useCallback((nodeId, position) => {
    setCanvasNodes(prev => prev.map(n => (n.id === nodeId ? { ...n, position } : n)));
    layoutRef.current.positions[nodeId] = position;
    persistLayout();
  }, [persistLayout]);

  /* ── Geçici kimliği kalıcısıyla değiştir ──
     Soru düğümü sorgu kimliği gelmeden önce çiziliyor (kullanıcı sorusunun
     tuvale düştüğünü hemen görmeli). Kimlik gelince düğüm, kenarları ve
     yerleşim kaydı yeni kimliğe taşınıyor; yoksa yerleşim yeniden
     yüklemede eşleşmezdi. */
  const renameNode = useCallback((oldId, newId) => {
    if (!oldId || !newId || oldId === newId) return;

    setCanvasNodes(prev => prev.map(n => (n.id === oldId ? { ...n, id: newId } : n)));

    const layout = layoutRef.current;
    if (layout.positions[oldId]) {
      layout.positions[newId] = layout.positions[oldId];
      delete layout.positions[oldId];
    }
    persistLayout();
  }, [persistLayout]);
  /* ── Yeni kart ──
     Sağ tıkta seçilen tür ve tıklanan yer. Kart sunucuya henüz gitmedi:
     kimliği `card:pending-…`, ilk cevap gelince gerçek sorgu kimliğine
     taşınıyor (yerleşim kimliğe göre saklanıyor). */
  const handleCreateCard = useCallback((chartType, position) => {
    const id = nodeIds.card(`pending-${Date.now()}`);
    setCanvasNodes(p => [...p, {
      id,
      type: 'biAnalysisCard',
      position,
      data: { chartType, turns: [], chart: null, queryId: null },
    }]);
    rememberPositions([{ id, position }]);
  }, [rememberPositions]);

  const patchCard = useCallback((id, patch) => {
    setCanvasNodes(p => p.map(n => (
      n.id === id
        ? { ...n, data: { ...n.data, ...(typeof patch === 'function' ? patch(n.data) : patch) } }
        : n
    )));
  }, []);

  /* ── Kartın içinden soru ──
     Yeni düğüm AÇMIYOR. Cevap bu kartın grafiğini güncelliyor, netleştirme
     ve hata da bu kartın konuşmasına düşüyor. Eskiden her tur tuvale ayrı
     kutular bırakıyordu ve üç soru sonra hangisinin hangisine ait olduğu
     okunmuyordu. */
  const askQuestion = useCallback(async (node, text) => {
    const connId = analysis?.connectionId || analysis?.requestId;
    if (!connId) return;

    const cardId = node.id;
    const stamp = Date.now();

    patchCard(cardId, d => ({
      turns: [...(d.turns || []), { role: 'user', content: text, ts: stamp }],
      loading: true,
      pendingConfirmations: [],
      needsRelationshipClarification: false,
      clarificationQuestion: null,
      confirmationStates: {},
      confirmationRejected: false,
    }));
    setQueryCount(c => c + 1);

    const say = (turn) => patchCard(cardId, d => ({
      turns: [...(d.turns || []), { ...turn, ts: Date.now() }],
      loading: false,
    }));

    try {
      const res = await askViaAgent(text, {
        connectionId: connId,
        // Zincirin ucu: kartın konuşması sunucuda da bir konuşma olarak
        // sürüyor, yoksa "bunu pasta yap" gibi devam soruları bağlamsız
        // kalırdı.
        parentQueryId: node.data?.queryId ?? null,
        onProgress: (message) => patchCard(cardId, { progress: message }),
      });

      // Kimlik gerçek sorgu kimliğine taşınıyor; kartın yerleşimi ve zinciri
      // yeniden yüklemede buradan bulunuyor.
      if (res.queryId && cardId.startsWith('card:pending-')) {
        renameNode(cardId, nodeIds.card(res.queryId));
      }
      const liveId = res.queryId && cardId.startsWith('card:pending-')
        ? nodeIds.card(res.queryId)
        : cardId;

      if (!res.success) {
        patchCard(liveId, d => ({
          turns: [...(d.turns || []), {
            role: 'ai',
            // Netleştirme hata değil, karşı soru: kırmızı göstermek
            // kullanıcıya yanlış bir şey yaptığını düşündürüyor.
            error: !res.needsClarification,
            content: res.error || 'Analiz tamamlanamadı.',
            ts: Date.now(),
          }],
          loading: false,
          progress: null,
          // Netleştirmede zincir SÜRÜYOR: kullanıcının cevabı bu tura
          // bağlanacak.
          queryId: res.queryId ?? d.queryId,
          pendingConfirmations: getPendingConfirmations(res),
          needsRelationshipClarification: Boolean(res.needsClarification && getPendingConfirmations(res).length),
          clarificationQuestion: res.needsClarification ? text : null,
          confirmationStates: {},
          confirmationRejected: false,
        }));
        return;
      }

      const chart = (res.charts || [])[0] ?? null;
      patchCard(liveId, d => ({
        turns: [...(d.turns || []), {
          role: 'ai',
          content: res.answer || (chart ? 'Grafik güncellendi.' : 'Sonuç üretilmedi.'),
          ts: Date.now(),
        }],
        // Grafik yalnızca YENİSİ geldiyse değişiyor: cevabı grafik
        // üretmeyen bir devam sorusu, ekrandaki grafiği silmemeli.
        chart: chart ?? d.chart,
        chartType: d.chartType || chart?.type || DEFAULT_CHART_TYPE,
        pendingConfirmations: getPendingConfirmations(res),
        needsRelationshipClarification: false,
        clarificationQuestion: null,
        confirmationStates: {},
        confirmationRejected: false,
        evidence: res.audit?.evidence ?? null,
        audit: res.audit ?? null,
        queryId: res.queryId ?? d.queryId,
        loading: false,
        progress: null,
      }));
    } catch (e) {
      say({ role: 'ai', error: true, content: e.message });
    } finally {
      setQueryCount(c => Math.max(0, c - 1));
    }
  }, [analysis, patchCard, renameNode]);

  /* Tür bir görünüm tercihi, verinin kendisi değil: soruyu yeniden sormadan
     değişebilmeli. */
  const handleCardChartType = useCallback((node, chartType) => {
    patchCard(node.id, { chartType });
  }, [patchCard]);

  const { handleAsk: handleCardAsk, handleConfirmMatch: handleNodeConfirmMatch } = useRelationshipConfirmations({
    connectionId: analysis?.connectionId,
    patchCard,
    askQuestion,
  });

  /* ── Silme ── */
  const handleNodeDelete = useCallback((node) => setPendingDelete(node), []);

  const confirmDelete = useCallback(() => {
    const node = pendingDelete;
    if (!node) return;

    // Kart kendi kendine yetiyor: konuşma da grafik de onun içinde, silinecek
    // bir dalı yok. Eskiden soru düğümü silinince cevabı ve grafikleri de
    // toplamak gerekiyordu — o düğümler artık yok.
    setCanvasNodes(p => p.filter(n => n.id !== node.id));

    const layout = layoutRef.current;
    layout.hidden = [...new Set([...layout.hidden, node.id])];
    delete layout.positions[node.id];
    persistLayout();

    setPendingDelete(null);
  }, [pendingDelete, persistLayout]);

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
          {/* Bağlantı okunamadıysa söyleniyor. Yan panel kalktı ama bu uyarı
              onunla birlikte gitmemeli: sebebi yazmayan boş bir tuval,
              kullanıcıyı kendi hatasını aramaya gönderir. */}
          {analysisMissing && (
            <span className="cp-topbar-warn">
              Bağlantı okunamadı — “Analiz Et” çalıştırılmamış olabilir.
            </span>
          )}
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
      </div>

      {/* ══ TUVAL ════════════════════════════════════════════
          `.cp-body` kalan yüksekliğin tamamını alıyor (flex: 1, height: 0);
          tuval de onun içinde %100. Yan panel kalktı ama bu sarmalayıcı
          KALMALI — tuval doğrudan `.cp-topbar` içine düşerse başlık çubuğu
          sabit yükseklikte olduğu için sıfır yüksekliğe sıkışıyor ve ekran
          bomboş görünüyor. */}
      <div className="cp-body">
        <main className="cp-canvas-area">
          <InfiniteCanvas
            nodes={canvasNodes}
            onNodeMove={handleNodeMove}
            onNodeAsk={handleCardAsk}
            onNodeDelete={handleNodeDelete}
            onNodeConfirmMatch={handleNodeConfirmMatch}
            onNodeChartType={handleCardChartType}
            onCreateCard={handleCreateCard}
          />
        </main>
      </div>

      {pendingDelete && (
        <DeleteConfirmDialog
          node={pendingDelete}
          onCancel={() => setPendingDelete(null)}
          onConfirm={confirmDelete}
        />
      )}
    </div>
  );
}
