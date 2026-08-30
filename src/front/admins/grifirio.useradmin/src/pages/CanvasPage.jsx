import React, { useState, useEffect, useRef, useCallback } from 'react';
import { useNavigate, useLocation } from 'react-router-dom';
import { IconArrowLeft, IconDatabase, IconLoader2 } from '@tabler/icons-react';
import InfiniteCanvas from '../components/Canvas/InfiniteCanvas';
import { loadLayout, saveLayout, applyLayout, emptyLayout } from '../components/Canvas/canvasLayout';
import { nodeIds } from '../components/Canvas/canvasGraph';
import { DEFAULT_CHART_TYPE } from '../components/Canvas/chartTypes';
import DeleteConfirmDialog from '../components/Canvas/DeleteConfirmDialog';
import {
  getConnectionSummary, getSelectedTables,
  submitAgentQuery, getAgentQueryStatus, getAgentQueryResult, getAgentQueryHistory,
  learnFact, listLearnedFacts,
} from '../services/dataAnalysisService';
import './CanvasPage.css';


/* Bir eşleştirmenin kimliği. Sunucudaki `LearnedFact.RelationshipKey` ile
   BİREBİR aynı olmak zorunda: aynı şeyi iki kez sormamak, iki tarafın aynı
   kimliği üretmesine bağlı. Yön korunuyor — a→b ile b→a aynı iddia değil.

   Köşeli parantezler baştan sona atılıyor, uçtan kırpılmıyor:
   `[dbo].[Musteriler]` ortadakileri de taşıyor ve kırpma onu
   `dbo].[musteriler` yapar. */
const normalizeName = (value) =>
  String(value ?? '').replace(/[[\]]/g, '').trim().toLowerCase();

const relationshipKey = (m) =>
  `rel:${normalizeName(m.fromTable)}.${normalizeName(m.fromColumn)}`
  + `->${normalizeName(m.toTable)}.${normalizeName(m.toColumn)}`;

const RESTORE_BASE_X = 80;
/* Kartlar alt alta diziliyor; kullanıcı sürüklerse yerleşim kaydediliyor. */

const CARD_GAP = 60;
const CARD_HEIGHT = 420;

/**
 * Sunucudan gelen sorgu geçmişini tuval durumuna çevirir.
 *
 * Saf fonksiyon: ağ çağrısı yapmaz, state'e dokunmaz. Ayrı durmasının sebebi
 * test edilebilirlik — kanvasın geri yüklenmesi gözle kolay doğrulanan bir
 * şey değil ve yanlış çalıştığında kullanıcı geçmişini kaybetmiş sanıyor.
 *
 * Bir KONUŞMA ZİNCİRİ = bir kart. Eskiden her tur tuvale ayrı düğümler
 * bırakıyordu (soru kutusu, cevap kutusu, grafik, uyarı) ve üç soru sonra
 * ekran okunmaz hale geliyordu. Zincirin tamamı artık tek kartın içinde:
 * konuşma solda, o konuşmanın ürettiği grafik sağda.
 *
 * Kartın kimliği zincirin KÖK sorgusundan geliyor; devam soruları yeni kart
 * açmıyor, var olanın konuşmasına ekleniyor.
 */
export const restoreFromHistory = (queries = []) => {
  const nodes = [];

  // Sorgu kimliği → ait olduğu kartın kök kimliği. Zincir tek yönlü
  // yazıldığı için ebeveyn her zaman çocuktan önce geliyor.
  const rootOf = new Map();
  const cards = new Map();  // kök kimliği → kart verisi

  for (const item of queries) {
    const root = (item.parentQueryId && rootOf.get(item.parentQueryId)) || item.queryId;
    rootOf.set(item.queryId, root);

    if (!cards.has(root)) {
      cards.set(root, { root, turns: [], chart: null, chartType: null, evidence: null, audit: null });
    }
    const card = cards.get(root);

    const ts = new Date(item.createdAt).getTime();
    card.turns.push({ role: 'user', content: item.question, ts });

    const result = item.result ?? {};

    if (item.status === 'completed') {
      card.turns.push({
        role: 'ai',
        content: result.summary || result.answer || 'Analiz tamamlandı.',
        ts,
      });

      // Zincirin SON grafiği kalıyor: devam soruları bir öncekini
      // düzeltiyor, iki cevabı yan yana bırakmak hangisinin geçerli
      // olduğunu belirsizleştirir.
      const chart = (result.charts || [])[0];
      if (chart) {
        card.chart = chart;
        card.chartType = chart.type || card.chartType;
      }
      // Kanıt notu yeniden yüklemede de duruyor: sayfa yenilenince kaybolan
      // bir uyarı, güvenilmez bir uyarıdır.
      card.evidence = result.audit?.evidence ?? null;
      card.audit = { ...(result.audit || {}), llmParameters: item.llmParameters };
    } else if (item.status === 'clarification') {
      card.turns.push({
        role: 'ai',
        content: item.clarificationQuestion
          || 'Bu soruyu çözemedim; biraz daha açık yazar mısınız?',
        ts,
      });
    } else {
      // Yarım kalmış sorgu da gösteriliyor: sessizce yutmak, kullanıcının
      // sorduğu bir soruyu hiç sorulmamış gibi göstermek olurdu.
      card.turns.push({
        role: 'ai', error: true, ts,
        content: result.error || 'Bu analiz tamamlanmadı.',
      });
    }

    // Zincirin ucu: devam sorusu buraya bağlanacak.
    card.queryId = item.queryId;
  }

  let y = 80;
  for (const card of cards.values()) {
    nodes.push({
      id: nodeIds.card(card.root),
      type: 'biAnalysisCard',
      position: { x: RESTORE_BASE_X, y },
      data: {
        turns: card.turns,
        chart: card.chart,
        chartType: card.chartType || DEFAULT_CHART_TYPE,
        evidence: card.evidence,
        audit: card.audit,
        queryId: card.queryId,
      },
    });
    y += CARD_HEIGHT + CARD_GAP;
  }

  return { nodes, nextPos: { x: RESTORE_BASE_X, y } };
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

  /* Hakkında karar verilmiş eşleştirmelerin anahtarları. Sunucudaki kayıtla
     aynı biçimde tutuluyor (bkz. LearnedFact.RelationshipKey) — aynı şeyi
     iki kez sormamanın tek yolu iki tarafın aynı kimliği üretmesi. */
  const answeredMatchesRef = useRef(new Set());

  /* Henüz cevaplanmamış eşleştirmeler. Ref okuduğu için useCallback'e gerek
     yok ve bağımlılık zinciri de kurmuyor. */
  const unanswered = (matches) =>
    (matches || []).filter(m => !answeredMatchesRef.current.has(relationshipKey(m)));

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

      // Hakkında zaten karar verilmiş eşleşmeler. Sözlükteki
      // `needsConfirmation` bayrağı ancak bir sonraki "Analiz Et"te
      // güncelleniyor; o zamana kadar aynı bağ her sorguda soru olarak
      // gelirdi. Üçüncü kez sorulan bir onay, okunmadan kapatılan bir
      // onaydır.
      try {
        const { items = [] } = await listLearnedFacts(connectionId);
        if (!cancelled) {
          answeredMatchesRef.current = new Set(
            items.filter(i => i.kind === 'relationship').map(i => i.key));
        }
      } catch {
        // Okunamazsa en kötüsü aynı soru bir kez daha sorulur; kanvasın
        // açılmasını engellememeli.
      }

      try {
        const { queries = [] } = await getAgentQueryHistory(connectionId);
        if (cancelled || queries.length === 0) return;

        const restored = restoreFromHistory(queries);
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
  const handleCardAsk = useCallback(async (node, text) => {
    const connId = analysis?.connectionId || analysis?.requestId;
    if (!connId) return;

    const cardId = node.id;
    const stamp = Date.now();

    patchCard(cardId, d => ({
      turns: [...(d.turns || []), { role: 'user', content: text, ts: stamp }],
      loading: true,
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
        pendingConfirmations: unanswered(res.audit?.pendingConfirmations),
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

  /* ── Eşleştirme onayı ──

     Kullanıcı sonuca bakıp "bu doğru" ya da "bu yanlış" diyor; ikisi de
     kaydediliyor. Hayır cevabını saklamak evet kadar önemli: unutulursa
     sistem aynı yanlış eşleşmeyi her analizde yeniden kurar ve aynı soruyu
     tekrar tekrar sorar.

     Kayıt bu sorguyu değiştirmiyor — ekrandaki grafik ne ise o kalıyor.
     Öğrenilen bilgi bir sonraki "Analiz Et"te sözlüğe işleniyor, çünkü bir
     ilişkinin geçerli olup olmadığı ancak veritabanına bakılarak, ölçüm
     kapısından geçirilerek bilinebilir. */
  const handleNodeConfirmMatch = useCallback(async (node, match, accepted) => {
    const connId = analysis?.connectionId || analysis?.requestId;
    if (!connId) return;

    try {
      await learnFact(connId, {
        kind: 'relationship',
        accepted,
        fromTable: match.fromTable,
        fromColumn: match.fromColumn,
        toTable: match.toTable,
        toColumn: match.toColumn,
        // Kartın konuşmasındaki son soru: bu eşleşme hangi soruya cevaben
        // kuruldu, kayıtta yazsın.
        question: [...(node.data?.turns || [])].reverse()
          .find(t => t.role === 'user')?.content ?? null,
      });

      // Bu tur boyunca ve sonraki sorgularda bir daha sorulmasın. Sunucudaki
      // `needsConfirmation` ancak bir sonraki "Analiz Et"te düşüyor.
      answeredMatchesRef.current.add(relationshipKey(match));

      // Soru cevaplandı: aynı düğümde bir daha görünmesin.
      setCanvasNodes(p => p.map(n => (
        n.id === node.id
          ? {
              ...n,
              data: {
                ...n.data,
                pendingConfirmations: (n.data?.pendingConfirmations || []).filter(
                  m => !(m.fromTable === match.fromTable && m.fromColumn === match.fromColumn
                      && m.toTable === match.toTable && m.toColumn === match.toColumn)),
              },
            }
          : n
      )));

      // Onayın sonucu kartın kendi konuşmasına yazılıyor: eskiden yan
      // paneldeki ortak akışa düşüyordu ve hangi grafiğin onayı olduğu
      // okunmuyordu.
      patchCard(node.id, d => ({
        turns: [...(d.turns || []), {
          role: 'ai',
          ts: Date.now(),
          content: accepted
            ? `${match.fromColumn} → ${match.toColumn} eşleşmesi hafızaya yazıldı. `
              + 'Bir sonraki "Analiz Et"ten itibaren bu bağlantı hazır olacak.'
            : `${match.fromColumn} → ${match.toColumn} eşleşmesi reddedildi olarak `
              + 'kaydedildi; bu bağlantı bir daha kurulmayacak.',
        }],
      }));
    } catch (e) {
      patchCard(node.id, d => ({
        turns: [...(d.turns || []), {
          role: 'ai', error: true, ts: Date.now(),
          content: `Kaydedilemedi: ${e.response?.data?.error || e.message}`,
        }],
      }));
    }
  }, [analysis, patchCard]);

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
