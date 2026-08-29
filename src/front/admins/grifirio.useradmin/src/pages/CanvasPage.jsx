import React, { useState, useEffect, useRef, useCallback } from 'react';
import { useNavigate, useLocation } from 'react-router-dom';
import { IconArrowLeft, IconDatabase, IconRobot, IconUser, IconSend, IconLoader2, IconChevronLeft, IconChevronRight } from '@tabler/icons-react';
import InfiniteCanvas from '../components/Canvas/InfiniteCanvas';
import { loadLayout, saveLayout, applyLayout, emptyLayout } from '../components/Canvas/canvasLayout';
import { nodeIds, collectSubtree, persistedQueryIdOf } from '../components/Canvas/canvasGraph';
import DeleteConfirmDialog from '../components/Canvas/DeleteConfirmDialog';
import {
  getConnectionById, getSelectedTables,
  submitAgentQuery, getAgentQueryStatus, getAgentQueryResult, getAgentQueryHistory,
} from '../services/dataAnalysisService';
import './CanvasPage.css';

/* Kaba yükseklikler — yalnızca yeni düğümün nereye konacağını kestirmek
   için. Kenarların çizimi gerçek ölçüme bakıyor (bkz. InfiniteCanvas). */
const NODE_HEIGHT = {
  biChartNode: 340,
  biTableNode: 310,
  biInsightNode: 180,
  biMetricNode: 150,
};
const heightOf = (node) => NODE_HEIGHT[node.type] ?? 200;

/* ─────────────────────────────────────────────────────────────
   Canvas-node builder helpers
   Render tamamen sonuç odaklıdır: backend'in döndürdüğü composite
   payload'daki answer + charts[] + insights[] neyse o çizilir.
───────────────────────────────────────────────────────────── */
const buildCanvasNodes = (report, parentId, posRef, queryId, sourceQuestion = '') => {
  const newNodes = [];
  const newEdges = [];
  const { x: sx, y: sy } = posRef.current;
  const COL_W = 430;
  const ROW_H_CHART = 330;
  const ROW_H_INSIGHT = 200;
  let curY = sy;

  const edge = (tgtId) => {
    if (!parentId) return;
    newEdges.push({ id: `e-${parentId}-${tgtId}`, source: parentId, target: tgtId, animated: true, style: { stroke: 'var(--accent)' } });
  };

  if (report.answer) {
    const id = nodeIds.answer(queryId);
    newNodes.push({ id, type: 'biInsightNode', position: { x: sx, y: curY }, data: { type: 'info', title: '🤖 AI Yanıtı', description: report.answer } });
    edge(id);
    curY += ROW_H_INSIGHT + 24;
  }

  (report.charts || []).forEach((chart, i) => {
    const id = nodeIds.chart(queryId, i);
    newNodes.push({
      id,
      type: 'biChartNode',
      position: { x: sx + (i % 2) * COL_W, y: curY + Math.floor(i / 2) * (ROW_H_CHART + 32) },
      // Grafiği doğuran soru düğümde duruyor: "bunu düzelt" dendiğinde
      // düzeltmenin neyin üzerine bindiğini bilmek gerekiyor.
      data: { ...chart, sourceQuestion },
    });
    edge(id);
  });
  if (report.charts?.length) curY += Math.ceil(report.charts.length / 2) * (ROW_H_CHART + 32);

  (report.insights || []).forEach((insight, i) => {
    const id = nodeIds.insight(queryId, i);
    newNodes.push({ id, type: 'biInsightNode', position: { x: sx + (i % 2) * COL_W, y: curY + Math.floor(i / 2) * (ROW_H_INSIGHT + 20) }, data: insight });
    edge(id);
  });
  if (report.insights?.length) curY += Math.ceil(report.insights.length / 2) * (ROW_H_INSIGHT + 20);

  posRef.current = { x: sx, y: Math.max(sy, curY) + 60 };
  return { newNodes, newEdges };
};

/**
 * Sunucudan gelen sorgu geçmişini tuval durumuna çevirir.
 *
 * Saf fonksiyon: ağ çağrısı yapmaz, state'e dokunmaz. Ayrı durmasının sebebi
 * test edilebilirlik — kanvasın geri yüklenmesi gözle kolay doğrulanan bir şey
 * değil ve yanlış çalıştığında kullanıcı geçmişini kaybetmiş sanıyor.
 *
 * Her soru kendi dalını açar: soru solda, cevap ve grafikler sağında.
 *
 * Sol panelden sorulan sorular birbirine BAĞLANMAZ — aralarında olmayan bir
 * ilişkiyi çizmek olurdu. Bağ yalnızca gerçekten kurulmuşsa var: bir soru bir
 * düğümün üzerinden sorulduysa sunucu bunu `parentQueryId` ile biliyor ve
 * zincir buradan geri kuruluyor. Önceden bilmiyordu; tuval her yeniden
 * yüklenişte konuşma, birbirinden bağımsız sorular yığınına dönüşüyordu.
 */
const CHAIN_INDENT = 40;
const CHAIN_MAX_LEVEL = 5;
const RESTORE_BASE_X = 80;
/* Cevap üretmemiş bir turun (netleştirme ya da hata) kapladığı dikey yer. */
const RESTORE_STUB_HEIGHT = 220;

export const restoreFromHistory = (queries = []) => {
  const messages = [];
  const nodes = [];
  const edges = [];
  const posRef = { current: { x: RESTORE_BASE_X, y: 80 } };

  /* Bir turun altına yazılan soru, o turun KULLANICIYA DÖNEN düğümüne
     bağlanıyor: cevap düğümü varsa ona, yoksa sorunun kendisine. Canlıda da
     kullanıcı o kutunun üzerinden devam ediyor. */
  const anchors = new Map();  // sorgu kimliği → bağlanacak düğüm
  const depths = new Map();   // sorgu kimliği → zincirdeki derinlik

  for (const item of queries) {
    const parentAnchor = item.parentQueryId ? anchors.get(item.parentQueryId) : null;
    const depth = depths.has(item.parentQueryId) ? depths.get(item.parentQueryId) + 1 : 0;
    depths.set(item.queryId, depth);

    const qNodeId = nodeIds.question(item.queryId);
    // Derinlik kadar içeri: zincirin nerede başlayıp nerede dallandığı, ok
    // takip etmeden de okunabilsin. Beşten sonra artmıyor — sonsuz kayan bir
    // sütun okunaklılığa katkı sağlamıyor.
    const qPos = {
      x: RESTORE_BASE_X + Math.min(depth, CHAIN_MAX_LEVEL) * CHAIN_INDENT,
      y: posRef.current.y,
    };
    const ts = new Date(item.createdAt).getTime();

    nodes.push({
      id: qNodeId, type: 'biInsightNode', position: qPos,
      data: { type: 'question', title: '💬 Soru', description: item.question },
    });

    if (parentAnchor) {
      // Kimlik öneki `chain-`: yerel yerleşimde daha kesin bir bağ varsa
      // (hangi DÜĞÜMÜN altına yazıldığı) bu kenar onun lehine düşürülüyor.
      edges.push({
        id: `chain-${parentAnchor}-${qNodeId}`,
        source: parentAnchor, target: qNodeId,
        animated: false, style: { stroke: 'var(--accent)' },
      });
    }

    messages.push({ role: 'user', content: item.question, ts });

    const result = item.result ?? {};

    if (item.status === 'completed') {
      const answer = result.summary || result.answer || 'Analiz tamamlandı.';
      messages.push({
        role: 'ai', content: answer, ts,
        result: { charts: result.charts || [] },
        audit: { ...(result.audit || {}), llmParameters: item.llmParameters },
      });

      posRef.current = { x: qPos.x + 460, y: qPos.y };
      const built = buildCanvasNodes(
        { answer, charts: result.charts || [], insights: [] },
        qNodeId, posRef, item.queryId, item.question);
      nodes.push(...built.newNodes);
      edges.push(...built.newEdges);
      posRef.current = { x: RESTORE_BASE_X, y: posRef.current.y };

      anchors.set(item.queryId, nodeIds.answer(item.queryId));
    } else if (item.status === 'clarification') {
      // Sistem soru sordu ve cevap bekliyor. Bu tur eskiden hiç
      // kaydedilmediği için tuvalde de yoktu: kullanıcı sayfayı yenilediğinde
      // sorulan soru kaybolur, cevabını yazacağı kutu da ortadan kalkardı.
      const asked = item.clarificationQuestion
        || 'Bu soruyu çözemedim; biraz daha açık yazar mısınız?';
      const askNodeId = nodeIds.answer(item.queryId);

      messages.push({ role: 'ai', content: asked, ts });

      nodes.push({
        id: askNodeId, type: 'biInsightNode',
        position: { x: qPos.x + 460, y: qPos.y },
        data: { type: 'warning', title: '💬 Bir sorum var', description: asked },
      });
      edges.push({
        id: `e-${qNodeId}-${askNodeId}`, source: qNodeId, target: askNodeId,
        animated: false, style: { stroke: 'var(--accent)' },
      });

      posRef.current = { x: RESTORE_BASE_X, y: qPos.y + RESTORE_STUB_HEIGHT };
      anchors.set(item.queryId, askNodeId);
    } else {
      // Yarım kalmış sorgu da gösteriliyor: sessizce yutmak, kullanıcının
      // sorduğu bir soruyu hiç sorulmamış gibi göstermek olurdu.
      messages.push({
        role: 'ai', error: true, ts,
        content: `❌ ${result.error || 'Bu analiz tamamlanmadı.'}`,
      });

      // Konum ilerletiliyor: ilerletilmediğinde sıradaki soru bu düğümün tam
      // üstüne biniyordu.
      posRef.current = { x: RESTORE_BASE_X, y: qPos.y + RESTORE_STUB_HEIGHT };
      anchors.set(item.queryId, qNodeId);
    }
  }

  /* Son tur bir netleştirmeyse konuşma cevap bekliyor demektir. Sayfa
     yenilendikten sonra da beklemeye devam etmeli: kullanıcı geri döndüğünde
     sistemin sorusu ekranda duruyor ama cevabı hiçbir yere bağlanmıyorsa,
     sistem sorduğunu yine unutmuş olur. */
  const last = queries[queries.length - 1];
  const pendingAsk = last && last.status === 'clarification'
    ? { queryId: last.queryId, nodeId: nodeIds.answer(last.queryId) }
    : null;

  return { messages, nodes, edges, pendingAsk, nextPos: { ...posRef.current } };
};

/**
 * Soruyu ajan hattina gonderir ve sonucu bekler.
 *
 * Hat asenkron: gonder -> kuyruga girer -> durum sorulur -> sonuc alinir.
 * Cagiran taraf tek bir Promise gorsun diye yoklama burada kapsulleniyor.
 */
const AGENT_POLL_MS = 3000;
const AGENT_MAX_ATTEMPTS = 100; // ~5 dakika

const askViaAgent = async (question, { connectionId, parentQueryId = null, onProgress }) => {
  if (!connectionId) {
    return { success: false, error: 'Bu kanvas bir bağlantıya bağlı değil.' };
  }

  // Sunucu 400 dondugunde sebebi govdede yaziyor ("Önce tabloları seçip Ön
  // Analiz çalıştırın" gibi). Axios bunu exception'a cevirdigi icin, yakalanip
  // acilmazsa kullaniciya yalnizca "Request failed with status code 400"
  // gorunuyordu — yani sunucu sebebi biliyor, ekran soylemiyordu.
  let submitted;
  try {
    submitted = await submitAgentQuery(connectionId, question, parentQueryId);
  } catch (err) {
    const body = err.response?.data;
    return {
      success: false,
      error: body?.error || body?.detail || body?.title || err.message,
      status: body?.status,
      // Sunucu soruyu cozemedigini soyluyor ve ne sormasi gerektigini
      // yaziyor. Bu bir hata degil, karsi soru — ekranda da oyle gorunmeli.
      needsClarification: Boolean(body?.needsClarification),
      // Netlestirme turu artik sunucuda kayitli ve kendi kimligi var.
      // Kullanicinin cevabi bu kimlige baglanacak; zincirin halkasi bu.
      queryId: body?.queryId,
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
        // Kimlik disari veriliyor: tuvaldeki gecici dugum kimlikleri bununla
        // kalicilariyla degistiriliyor, yerlesim de o kimliklere yazilıyor.
        queryId,
        answer: result.summary || result.answer || 'Analiz tamamlandı.',
        charts: result.charts || [],
        failedTasks: [],
        // Denetim izi: hangi tablo/kolon secildi, hangi SQL calisti.
        audit: { ...(result.audit || {}), llmParameters: payload?.llmParameters },
      };
    }

    if (status.status === 'failed') {
      return { success: false, queryId, error: status.message || status.error || 'Analiz başarısız oldu.' };
    }

    onProgress?.('Analiz ediliyor…');
  }

  return { success: false, queryId, error: 'Zaman aşımı — analiz 5 dakikada tamamlanmadı.' };
};

/* Denetim panelinde filtreleri okunabilir yazar.

   Analizör zaten okunabilir bir özet üretiyor (`appliedFilters`) — hangi
   kolona hangi karşılaştırmanın uygulandığını orada yazıyor. Varsa o
   kullanılıyor.

   Yoksa ham `filters` biçimlendiriliyor. Bu nesne artık yalnızca düz değer
   tutmuyor: tarih aralıkları `{gte, lt}`, çoklu seçim ise dizi olarak
   geliyor. Şablon dizesiyle yazdırmak bunları `[object Object]` yapıyordu —
   yani "bu yıl" diye sorulduğunda denetim panelinde filtrenin ne olduğu
   okunamıyordu, ki panelin varlık sebebi tam olarak o. */
const FILTER_OPS = { gte: '≥', gt: '>', lte: '≤', lt: '<', eq: '=', ne: '≠' };

const describeFilters = (audit) => {
  if (Array.isArray(audit?.appliedFilters) && audit.appliedFilters.length > 0) {
    return audit.appliedFilters.join(' · ');
  }

  const filters = audit?.filters;
  if (!filters || Object.keys(filters).length === 0) return '';

  return Object.entries(filters)
    .map(([column, value]) => {
      if (value === null || value === undefined) return `${column} boş`;
      if (Array.isArray(value)) return `${column} ∈ (${value.join(', ')})`;
      if (typeof value === 'object') {
        return Object.entries(value)
          .map(([op, operand]) =>
            `${column} ${FILTER_OPS[String(op).toLowerCase()] ?? op} ${operand}`)
          .join(' ve ');
      }
      return `${column} = ${value}`;
    })
    .join(' · ');
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

  /* Cevap bekleyen soru — sistem sordu, kullanıcı henüz yanıtlamadı.

     Sol panelden gelen sorular normalde birbirine bağlanmaz; aralarında
     olmayan bir ilişkiyi çizmek olurdu. Ama sistem BİR SORU SORDUYSA,
     kullanıcının panele yazdığı sonraki şey neredeyse her zaman o sorunun
     cevabıdır ve "hangi düğümün altına yazdın" diye beklemek anlamsız —
     kullanıcı en doğal yere, sohbet kutusuna yazıyor. Bağlamamak, sistemin
     sorduğunu unutmasıyla aynı sonucu verirdi.

     `{ queryId, nodeId }` — biri zinciri sunucuda, öteki tuvalde kuruyor. */
  const pendingAskRef = useRef(null);

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
        const { queries = [] } = await getAgentQueryHistory(connectionId);
        if (cancelled || queries.length === 0) return;

        const restored = restoreFromHistory(queries);
        const laidOut = applyLayout(restored, layout);

        setMessages(restored.messages);
        setCanvasNodes(laidOut.nodes);
        setCanvasEdges(laidOut.edges);
        nextPosRef.current = restored.nextPos;
        // Sistemin sorusu cevapsız kaldıysa beklemeye kaldığı yerden devam
        // ediyor — kullanıcının cevabı yine o tura bağlanacak.
        pendingAskRef.current = restored.pendingAsk;
      } catch {
        // Geçmiş okunamazsa kanvas boş açılır ve yeni soru sorulabilir.
        // Eski sohbeti gösterememek, ekranı tamamen kilitlemekten iyidir.
      }
    })();

    return () => { cancelled = true; };
  }, [connectionId]);

  /* Auto-scroll chat */
  useEffect(() => {
    chatEndRef.current?.scrollIntoView({ behavior: 'smooth' });
  }, [messages]);

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
    setCanvasEdges(prev => prev.map(e => (
      e.source === oldId || e.target === oldId
        ? {
            ...e,
            id: e.id.split(oldId).join(newId),
            source: e.source === oldId ? newId : e.source,
            target: e.target === oldId ? newId : e.target,
          }
        : e
    )));

    const layout = layoutRef.current;
    if (layout.positions[oldId]) {
      layout.positions[newId] = layout.positions[oldId];
      delete layout.positions[oldId];
    }
    if (layout.parents[oldId]) {
      layout.parents[newId] = layout.parents[oldId];
      delete layout.parents[oldId];
    }
    for (const [child, parent] of Object.entries(layout.parents)) {
      if (parent === oldId) layout.parents[child] = newId;
    }
    persistLayout();
  }, [persistLayout]);

  /* ── Soruyu çalıştır ──
     Üç giriş noktası da buraya geliyor: sol panel, soru düğümündeki kutu
     ve hızlı sorular. Tek fark bağlanacağı ebeveyn ve konum. */
  const runAsk = useCallback(async ({ text, parentId = null, anchor, origin = 'panel' }) => {
    const connId = analysis?.connectionId || analysis?.requestId;
    const stamp = Date.now();

    // Ebeveyn düğümün kimliği hangi sorgudan geliyorsa konuşmanın önceki turu
    // odur: soru düğümünün de, o soruya ait grafiğin de altına yazmak aynı
    // tura devam etmek demek.
    //
    // Tuvalden bir düğüm gösterilmediyse cevap bekleyen soruya bakılıyor:
    // sistem sorduysa, panele yazılan şey o sorunun cevabıdır.
    const pendingAsk = parentId ? null : pendingAskRef.current;
    const linkTo = parentId || pendingAsk?.nodeId || null;
    const parentQueryId = parentId
      ? persistedQueryIdOf(parentId)
      : (pendingAsk?.queryId ?? null);

    setMessages(p => [
      ...p,
      { role: 'user', content: text, ts: stamp, origin },
      { role: 'ai', content: '', loading: true, ts: stamp },
    ]);
    setQueryCount(c => c + 1);

    let qNodeId = `q:pending-${stamp}`;
    const loadingNodeId = `chart:pending-${stamp}:0`;
    const qPos = { ...anchor };

    setCanvasNodes(p => [
      ...p,
      {
        id: qNodeId, type: 'biInsightNode', position: qPos,
        data: { type: 'question', title: '💬 Soru', description: text },
      },
      {
        id: loadingNodeId, type: 'biChartNode',
        position: { x: qPos.x + 460, y: qPos.y },
        data: { loading: true, title: 'Analiz ediliyor…' },
      },
    ]);
    setCanvasEdges(p => [
      ...p,
      // Sol panelden gelen soru bir öncekine bağlanmıyor — cevap bekleyen
      // bir soru yoksa. Bağ ya tuval üzerinden kuruluyor ya da sistemin
      // sorduğu soruya verilen cevapla.
      ...(linkTo ? [{
        id: `e-${linkTo}-${qNodeId}`, source: linkTo, target: qNodeId,
        animated: false, style: { stroke: 'var(--accent)' },
      }] : []),
      {
        id: `e-${qNodeId}-${loadingNodeId}`, source: qNodeId, target: loadingNodeId,
        animated: true, style: { stroke: 'var(--accent)' },
      },
    ]);

    try {
      const res = await askViaAgent(text, {
        connectionId: connId,
        parentQueryId,
        onProgress: (message) => {
          if (!message) return;
          setCanvasNodes(p => p.map(n =>
            n.id === loadingNodeId ? { ...n, data: { ...n.data, title: message } } : n));
        },
      });

      if (res.queryId) {
        const finalId = nodeIds.question(res.queryId);
        renameNode(qNodeId, finalId);
        qNodeId = finalId;
      }
      if (linkTo) {
        layoutRef.current.parents[qNodeId] = linkTo;
      }

      if (res.success) {
        // Cevap üretildi: ortada bekleyen bir soru kalmadı.
        pendingAskRef.current = null;

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

        const posRef = { current: { x: qPos.x + 460, y: qPos.y } };
        const { newNodes, newEdges } = buildCanvasNodes(report, qNodeId, posRef, res.queryId, text);
        setCanvasNodes(p => [...p, ...newNodes]);
        setCanvasEdges(p => [...p, ...newEdges]);

        rememberPositions([{ id: qNodeId, position: qPos }, ...newNodes]);

        // Sol panelden gelen sorular yukarıdan aşağıya diziliyor; tuval
        // üzerinden açılan dal kendi yerini kendisi seçtiği için sıradaki
        // boş satırı kaydırmıyor.
        if (origin === 'panel') {
          nextPosRef.current = { x: qPos.x, y: posRef.current.y };
        }
      } else {
        // Netleştirme, hatadan farklı: sistem çalıştı ama soruyu çözemedi ve
        // ne sorması gerektiğini biliyor. Kırmızı "Hata" göstermek kullanıcıya
        // yanlış bir şey yaptığını düşündürüyor — oysa yapılacak tek şey
        // soruyu biraz daha açık yazmak.
        const asksBack = res.needsClarification;
        const label = asksBack ? '💬 Bir sorum var' : '❌ Hata';
        const body = res.error || 'Hata oluştu';

        // Netleştirme düğümü konuşmanın bir halkası: kullanıcı cevabını bunun
        // üzerinden yazacak ve o cevap bu tura bağlanacak. Bunun için düğümün
        // geçici kimlikten sunucudaki gerçek kimliğe taşınması gerekiyor —
        // `pending-…` kimliği hem zinciri kuramaz hem yeniden yüklemede kaybolur.
        const askNodeId = asksBack && res.queryId
          ? nodeIds.answer(res.queryId)
          : loadingNodeId;
        const askPos = { x: qPos.x + 460, y: qPos.y };
        renameNode(loadingNodeId, askNodeId);

        // Cevap bekleyen soru güncelleniyor. Hata durumunda temizleniyor:
        // ortada cevaplanacak bir soru yok, sonraki mesaj yeni bir sorudur.
        pendingAskRef.current = (asksBack && res.queryId)
          ? { queryId: res.queryId, nodeId: askNodeId }
          : null;

        setCanvasNodes(p => p.map(n =>
          n.id === askNodeId
            ? { ...n, type: 'biInsightNode', data: { type: asksBack ? 'warning' : 'error', title: label, description: body } }
            : n
        ));
        setCanvasEdges(p => p.map(e => (e.target === askNodeId ? { ...e, animated: false } : e)));
        rememberPositions(askNodeId === loadingNodeId
          ? [{ id: qNodeId, position: qPos }]
          : [{ id: qNodeId, position: qPos }, { id: askNodeId, position: askPos }]);

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

        if (origin === 'panel') {
          nextPosRef.current = { x: qPos.x, y: qPos.y + 400 };
        }
      }
    } catch (e) {
      pendingAskRef.current = null;
      setCanvasNodes(p => p.map(n =>
        n.id === loadingNodeId
          ? { ...n, type: 'biInsightNode', data: { type: 'error', title: '❌ Hata', description: e.message } }
          : n
      ));
      setCanvasEdges(p => p.map(e2 => (e2.target === loadingNodeId ? { ...e2, animated: false } : e2)));
      setMessages(p => {
        const a = [...p];
        a[a.length - 1] = { role: 'ai', content: `❌ ${e.message}`, error: true, ts: Date.now() };
        return a;
      });
    } finally {
      setQueryCount(c => Math.max(0, c - 1));
    }
  }, [analysis, renameNode, rememberPositions]);

  /* ── Sol panelden soru ── */
  const handleAsk = (q) => {
    const text = (q || question).trim();
    if (!text) return;

    setQuestion('');
    if (inputRef.current) inputRef.current.style.height = 'auto';

    runAsk({ text, anchor: { ...nextPosRef.current }, origin: 'panel' });
  };

  /* ── Soru düğümünün üzerinden devam ──
     Yeni dal, ebeveynin altındaki ilk boş yere kuruluyor: dalın tamamı
     (soru + cevabı + grafikleri) hesaba katılıyor, yoksa üstüne biniyordu. */
  const handleNodeAsk = useCallback((node, text) => {
    const ids = collectSubtree(canvasNodes, canvasEdges, node.id);
    const members = canvasNodes.filter(n => ids.has(n.id));
    const bottom = members.length > 0
      ? Math.max(...members.map(n => n.position.y + heightOf(n)))
      : node.position.y + heightOf(node);

    runAsk({
      text,
      parentId: node.id,
      anchor: { x: node.position.x + 60, y: bottom + 60 },
      origin: 'canvas',
    });
  }, [canvasNodes, canvasEdges, runAsk]);

  /* ── Grafiği yerinde düzelt ──
     Yanlış anlaşılmış bir soru için ikinci bir grafik eklemek, tuvalde aynı
     sorunun iki cevabını yan yana bırakıyor ve hangisinin geçerli olduğu
     kaybolyor. Düzeltme sonucu bu düğümün yerine geçiyor.

     Bağlam artık soruya gömülmüyor. Önceden asıl soru metni düzeltmenin önüne
     yapıştırılıyordu, çünkü ajan hattı sohbet geçmişi almıyordu; şimdi alıyor
     ve düzeltilen grafiği doğuran tur `parentQueryId` ile gösteriliyor. Fark
     yalnızca temizlik değil: prompt'a giden şey artık o turun metni değil,
     ürettiği parametrelerin tamamı — model üzerine ekleme yapabiliyor. */
  const handleNodeRefine = useCallback(async (node, text) => {
    const connId = analysis?.connectionId || analysis?.requestId;
    const parentQueryId = persistedQueryIdOf(node.id);
    // Zincir kurulamıyorsa (kimliği sunucuya hiç yazılmamış bir düğüm) eski
    // yönteme dönülüyor: bağlamsız düzeltme, yanlış grafikten de kötü.
    const original = node.data?.sourceQuestion;
    const composed = (!parentQueryId && original)
      ? `${original}\n\nDüzeltme isteği: ${text}`
      : text;
    const previousData = node.data;
    const stamp = Date.now();

    setMessages(p => [
      ...p,
      { role: 'user', content: text, ts: stamp, origin: 'refine' },
      { role: 'ai', content: '', loading: true, ts: stamp },
    ]);
    setQueryCount(c => c + 1);
    setCanvasNodes(p => p.map(n => (
      n.id === node.id
        ? { ...n, data: { loading: true, title: 'Grafik yeniden hesaplanıyor…' } }
        : n
    )));

    try {
      const res = await askViaAgent(composed, {
        connectionId: connId,
        parentQueryId,
        onProgress: (message) => {
          if (!message) return;
          setCanvasNodes(p => p.map(n =>
            n.id === node.id ? { ...n, data: { ...n.data, title: message } } : n));
        },
      });

      if (!res.success || (res.charts || []).length === 0) {
        // Düzeltme başarısızsa eski grafik geri geliyor: kullanıcıyı elinde
        // olan sonuçtan da etmek, yanlış grafikten kötü.
        setCanvasNodes(p => p.map(n => (n.id === node.id ? { ...n, data: previousData } : n)));
        const body = res.success
          ? 'Düzeltme bir grafik üretmedi; grafik olduğu gibi bırakıldı.'
          : (res.error || 'Düzeltme başarısız oldu.');
        setMessages(p => {
          const a = [...p];
          a[a.length - 1] = { role: 'ai', content: `❌ ${body}`, error: true, ts: Date.now() };
          return a;
        });
        return;
      }

      const [first, ...extras] = res.charts;
      const answer = res.answer || '';

      setCanvasNodes(p => p.map(n => (
        n.id === node.id ? { ...n, data: { ...first, sourceQuestion: composed } } : n
      )));

      // Bu grafiğin bağlı olduğu soru düğümü ve o dalın yorum düğümü.
      const parentId = canvasEdges.find(e => e.target === node.id)?.source ?? null;

      // Yorum düğümü eski grafiği anlatıyordu; grafik değişince o cümle de
      // yanlış oluyor. Aynı dalda duran yorum güncelleniyor.
      if (parentId && answer) {
        setCanvasNodes(p => p.map(n => (
          n.id.startsWith('ans:') && canvasEdges.some(e => e.source === parentId && e.target === n.id)
            ? { ...n, data: { ...n.data, description: answer } }
            : n
        )));
      }

      // Düzeltme birden fazla grafik döndürürse fazlası aynı dala ekleniyor.
      if (extras.length > 0) {
        const extraNodes = extras.map((chart, i) => ({
          id: nodeIds.chart(res.queryId, i + 1),
          type: 'biChartNode',
          position: { x: node.position.x, y: node.position.y + (i + 1) * 370 },
          data: { ...chart, sourceQuestion: composed },
        }));
        setCanvasNodes(p => [...p, ...extraNodes]);
        if (parentId) {
          setCanvasEdges(p => [...p, ...extraNodes.map(n => ({
            id: `e-${parentId}-${n.id}`, source: parentId, target: n.id,
            animated: true, style: { stroke: 'var(--accent)' },
          }))]);
        }
        rememberPositions(extraNodes);
      }

      // Yeniden yüklemede: düzeltmenin kendi soru dalı gizleniyor, bu düğüm
      // ise düzeltme sorgusunun grafiğiyle dolduruluyor.
      layoutRef.current.replacements[node.id] = res.queryId;
      layoutRef.current.hiddenQueries = [
        ...new Set([...(layoutRef.current.hiddenQueries ?? []), res.queryId]),
      ];
      persistLayout();

      setMessages(p => {
        const a = [...p];
        a[a.length - 1] = {
          role: 'ai', content: answer, ts: Date.now(),
          result: { charts: res.charts }, audit: res.audit || null, replaced: true,
        };
        return a;
      });
    } catch (e) {
      setCanvasNodes(p => p.map(n => (n.id === node.id ? { ...n, data: previousData } : n)));
      setMessages(p => {
        const a = [...p];
        a[a.length - 1] = { role: 'ai', content: `❌ ${e.message}`, error: true, ts: Date.now() };
        return a;
      });
    } finally {
      setQueryCount(c => Math.max(0, c - 1));
    }
  }, [analysis, canvasEdges, persistLayout, rememberPositions]);

  /* ── Silme ── */
  const handleNodeDelete = useCallback((node) => setPendingDelete(node), []);

  const confirmDelete = useCallback(() => {
    const node = pendingDelete;
    if (!node) return;

    // Soru düğümü silinince dalın tamamı gidiyor: cevabı ve grafikleri
    // tuvalde bırakmak, neyin sorulduğu bilinmeyen kutular demek.
    const doomed = node.data?.type === 'question'
      ? collectSubtree(canvasNodes, canvasEdges, node.id)
      : new Set([node.id]);

    setCanvasNodes(p => p.filter(n => !doomed.has(n.id)));
    setCanvasEdges(p => p.filter(e => !doomed.has(e.source) && !doomed.has(e.target)));

    const layout = layoutRef.current;
    layout.hidden = [...new Set([...layout.hidden, ...doomed])];
    for (const id of doomed) {
      delete layout.positions[id];
      delete layout.parents[id];
      delete layout.replacements[id];
    }
    persistLayout();

    setPendingDelete(null);
  }, [pendingDelete, canvasNodes, canvasEdges, persistLayout]);

  const QUICK_Q = ['En aktif kullanıcılar?', 'Aylık veri artışı?', 'En büyük tablo?'];

  const ORIGIN_LABEL = {
    canvas: '🧵 Tuvalde bir sorunun devamı',
    refine: '✎ Bir grafiğin düzeltmesi',
  };

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

                      {/* Sohbet, tuvalde olan biteni de kaydediyor: aynı soru
                          listesinde hangi sorunun tuval üzerinden sorulduğu
                          görünmezse geçmiş yanıltıcı olur. */}
                      {ORIGIN_LABEL[msg.origin] && (
                        <div className="cp-msg-origin">{ORIGIN_LABEL[msg.origin]}</div>
                      )}

                      {msg.replaced && (
                        <div className="cp-msg-meta">✎ Grafik tuvalde güncellendi</div>
                      )}
                      {!msg.replaced && msg.result?.charts?.length > 0 && (
                        <div className="cp-msg-meta">📊 {msg.result.charts.length} grafik tuvale eklendi</div>
                      )}

                      {msg.needsPreAnalysis && (
                        <button
                          className="cp-quick-q"
                          style={{ marginTop: 8, width: '100%' }}
                          onClick={() => navigate('/data?tab=connections')}
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
                            {/* Birlestirme, sonucun dogrulugunu en cok
                                etkileyen ama en az gorunen karar: hangi
                                tablonun hangi yoldan baglandigi, baglantinin
                                olculmus mu cikarsanmis mi oldugu. Sekiz
                                tablolu bir sorguda "bu sayi dogru mu"
                                sorusunu baska turlu cevaplamak mumkun degil. */}
                            {msg.audit.joins?.length > 0 && (
                              <div>
                                <dt>Birleştirme</dt>
                                <dd>
                                  {msg.audit.joins.map((note, i) => (
                                    <div key={i}>{note}</div>
                                  ))}
                                </dd>
                              </div>
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
                            {describeFilters(msg.audit) && (
                              <div><dt>Filtre</dt><dd>{describeFilters(msg.audit)}</dd></div>
                            )}
                            {/* Eşik ayrı satır: filtreyle karıştırılması en
                                kolay şey. Filtre satırları toplamadan önce
                                eler, eşik grupları toplandıktan sonra —
                                ikisi farklı soruları cevaplar ve hangisinin
                                uygulandığı buradan görülmeli. */}
                            {msg.audit.having && (
                              <div><dt>Eşik</dt><dd>{msg.audit.having}</dd></div>
                            )}
                            {msg.audit.window && (
                              <div><dt>Kırılım üstü hesap</dt><dd>{msg.audit.window}</dd></div>
                            )}
                            {/* Birleşimde grafikteki her seri ayrı bir
                                tablodan geliyor; hangisinin nereden geldiği
                                söylenmezse iki seri tek veri sanılır. */}
                            {msg.audit.union?.length > 0 && (
                              <div>
                                <dt>Kaynaklar</dt>
                                <dd>
                                  {msg.audit.union.map((note, i) => (
                                    <div key={i}>{note}</div>
                                  ))}
                                </dd>
                              </div>
                            )}
                            {typeof msg.audit.groupCount === 'number' && msg.audit.groupCount > 0 && (
                              <div><dt>Toplam grup</dt><dd>{msg.audit.groupCount}</dd></div>
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

                          {/* Okuma tavanina degilmis: sonuc tablonun tamamini
                              temsil etmiyor. Grafigin dogru gorunmesi bunu
                              gizliyor, o yuzden acikca yaziliyor. */}
                          {msg.audit.truncated && (
                            <p className="cp-audit-warn">
                              ⚠ Okuma tavanına ulaşıldı; tablodan yalnızca ilk{' '}
                              {msg.audit.rowsRead} satır okundu. Sonucu daraltmak için
                              tarih ya da kategori filtresi ekleyin.
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
          <InfiniteCanvas
            nodes={canvasNodes}
            edges={canvasEdges}
            onNodeMove={handleNodeMove}
            onNodeAsk={handleNodeAsk}
            onNodeRefine={handleNodeRefine}
            onNodeDelete={handleNodeDelete}
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
