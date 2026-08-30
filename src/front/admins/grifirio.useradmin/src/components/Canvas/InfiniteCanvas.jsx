import React, { useState, useRef, useCallback, useEffect } from 'react';
import BiAnalysisCard from './nodes/BiAnalysisCard';
import CanvasContextMenu from './CanvasContextMenu';
import './Canvas.css';

const NODE_COMPONENTS = {
  biAnalysisCard: BiAnalysisCard,
};

/* Kenar uçları düğümün gerçek boyutundan hesaplanıyor; ölçüm gelene kadar
   (ilk kare) bu değerler kullanılıyor. Düğümler artık sabit yükseklikte
   değil — soru düğümünde soru kutusu var, grafik düğümünde düzeltme
   kutusu açılıp kapanıyor. */
const DEFAULT_SIZE = {
  biAnalysisCard: { w: 760, h: 420 },
};
const FALLBACK_SIZE = { w: 380, h: 160 };

const SVG_SIZE = 20000;
const MIN_ZOOM = 0.15;
const MAX_ZOOM = 3;

/* Sürüklemeyi tıklamadan ayıran eşik (ekran pikseli). Bunun altındaki
   hareket tıklama sayılır — fare basılıyken elin birkaç piksel kayması
   düğümü yerinden oynatmasın. */
const DRAG_THRESHOLD = 3;

/* Bu öğelerin üzerinde basılan fare sürükleme başlatmaz: yazı seçmek,
   düğmeye basmak ve grafiğin üzerinde gezinmek çalışmaya devam etmeli.
   `canvas` Chart.js'in çizim yüzeyi — ipuçları oradan okunuyor. */
const NO_DRAG_SELECTOR = 'input, textarea, button, select, a, canvas, summary, details';

export default function InfiniteCanvas({
  nodes = [], edges = [], onNodeClick,
  onNodeMove, onNodeAsk, onNodeDelete, onNodeConfirmMatch,
  onNodeChartType, onCreateCard,
}) {
  /* ── Sağ tık menüsü ──
     Tuvale girişin tek yolu bu. Yan paneldeki sohbet kaldırıldı: bütün
     kartların soruları tek akışta toplandığı için ayrı konuların cümleleri
     alt alta düşüyor ve okunmuyordu. Artık her konuşma kendi kartının
     içinde duruyor ve kart, kullanıcının sağ tıkladığı yere kuruluyor. */
  const [menu, setMenu] = useState(null);
  const [transform, setTransform] = useState({ x: 60, y: 60, zoom: 1 });
  const [isPanning, setIsPanning] = useState(false);
  const startPanRef = useRef({ x: 0, y: 0 });
  const canvasRef = useRef(null);

  // Sürükleme sırasında güncel yakınlaşmayı okumak için: fare olayları
  // pencereye bağlanıyor ve o kapanış `transform`u eskitirdi.
  const transformRef = useRef(transform);
  transformRef.current = transform;

  /* ── Düğüm boyutları ──
     Kenarların düğümün ortasından çıkması için gerçek yükseklik gerekiyor.
     Düğümler artık sabit yükseklikte değil: soru kutusu, düzeltme kutusu ve
     uzun metinler boyu değiştiriyor. Sabit sayı kullanılırsa oklar
     düğümlerin ortasını değil, olmayan bir noktayı gösteriyor.

     İki kaynak var. Yerleşim effect'i her çizimden sonra ölçüyor — düğüm
     eklenip çıktığında bu yeterli. ResizeObserver ise düğümün KENDİ
     durumundan doğan değişimi yakalıyor (düzeltme kutusunun açılması gibi);
     o değişim InfiniteCanvas'ı yeniden çizdirmediği için effect kaçırırdı.
     Gözlemcinin bulunmadığı ortamda ölçüm yine de çalışıyor. */
  const [sizes, setSizes] = useState({});
  const sizesRef = useRef({});
  const elementsRef = useRef(new Map());

  const measureAll = useCallback(() => {
    const next = { ...sizesRef.current };
    let changed = false;

    for (const [id, el] of elementsRef.current) {
      const w = el.offsetWidth;
      const h = el.offsetHeight;
      if (!w && !h) continue; // henüz yerleşmemiş
      const prev = next[id];
      if (!prev || Math.abs(prev.w - w) > 0.5 || Math.abs(prev.h - h) > 0.5) {
        next[id] = { w, h };
        changed = true;
      }
    }
    for (const id of Object.keys(next)) {
      if (!elementsRef.current.has(id)) {
        delete next[id];
        changed = true;
      }
    }

    if (changed) {
      sizesRef.current = next;
      setSizes(next);
    }
  }, []);

  const observerRef = useRef(null);
  if (observerRef.current === null && typeof ResizeObserver !== 'undefined') {
    observerRef.current = new ResizeObserver(measureAll);
  }
  useEffect(() => () => observerRef.current?.disconnect(), []);

  const measureRef = useCallback((el) => {
    if (!el) return undefined;
    const id = el.dataset.nodeId;
    elementsRef.current.set(id, el);
    observerRef.current?.observe(el);
    return () => {
      elementsRef.current.delete(id);
      observerRef.current?.unobserve(el);
    };
  }, []);

  // Bilerek bağımlılıksız: her çizimden sonra ölçüyor. Boyut değişmediğinde
  // state'e dokunulmadığı için döngü olmuyor.
  useEffect(measureAll);

  const sizeOf = (node) =>
    sizes[node.id] || DEFAULT_SIZE[node.type] || FALLBACK_SIZE;

  /* ── Pan ── */
  const onMouseDown = useCallback((e) => {
    // Only pan on background clicks (not on a node)
    if (e.target.closest('.canvas-node')) return;
    setIsPanning(true);
    startPanRef.current = { x: e.clientX - transform.x, y: e.clientY - transform.y };
    e.preventDefault();
  }, [transform]);

  const onMouseMove = useCallback((e) => {
    if (!isPanning) return;
    setTransform(prev => ({
      ...prev,
      x: e.clientX - startPanRef.current.x,
      y: e.clientY - startPanRef.current.y,
    }));
  }, [isPanning]);

  const onMouseUp = useCallback(() => setIsPanning(false), []);

  /* ── Düğüm sürükleme ──
     Sonuçlar tuvale yukarıdan aşağıya diziliyor; bu dizilim bir öneri,
     kural değil. Kullanıcı hangi grafiği hangisinin yanında görmek
     istediğini kendi seçebilmeli.

     Fare olayları pencereye bağlanıyor: imleç düğümün dışına taştığında
     sürükleme kopmasın. Yer değiştirme ekran pikselinden tuval birimine
     çevriliyor — uzaklaşmış tuvalde 1 piksel fare hareketi daha fazla yol
     demek. */
  const dragRef = useRef(null);
  const endDragRef = useRef(null);
  const [draggingId, setDraggingId] = useState(null);

  const startDrag = useCallback((e, node) => {
    if (!onNodeMove || e.button !== 0) return;
    if (e.target.closest(NO_DRAG_SELECTOR)) return;

    e.stopPropagation();
    e.preventDefault();

    const drag = {
      id: node.id,
      startX: e.clientX,
      startY: e.clientY,
      originX: node.position.x,
      originY: node.position.y,
      moved: false,
    };
    dragRef.current = drag;

    // Dinleyiciler bir effect'te değil, tam burada bağlanıyor: effect ancak
    // React yeniden çizdikten sonra çalışır ve o ana kadar gelen fare
    // hareketleri düşerdi — sürükleme ilk piksellerde takılmış görünürdü.
    const onWindowMove = (ev) => {
      const screenDx = ev.clientX - drag.startX;
      const screenDy = ev.clientY - drag.startY;
      if (!drag.moved && Math.hypot(screenDx, screenDy) < DRAG_THRESHOLD) return;
      drag.moved = true;

      const zoom = transformRef.current.zoom || 1;
      onNodeMove(drag.id, {
        x: Math.round(drag.originX + screenDx / zoom),
        y: Math.round(drag.originY + screenDy / zoom),
      });
    };

    const onWindowUp = () => {
      window.removeEventListener('mousemove', onWindowMove);
      window.removeEventListener('mouseup', onWindowUp);
      endDragRef.current = null;
      setDraggingId(null);
    };

    window.addEventListener('mousemove', onWindowMove);
    window.addEventListener('mouseup', onWindowUp);
    endDragRef.current = onWindowUp;
    setDraggingId(node.id);
  }, [onNodeMove]);

  // Sürükleme sürerken bileşen sökülürse dinleyiciler pencerede kalmasın.
  useEffect(() => () => endDragRef.current?.(), []);

  /* ── Fit-to-screen ── */
  const fitView = useCallback(() => {
    if (nodes.length === 0) {
      setTransform({ x: 60, y: 60, zoom: 1 });
      return;
    }
    const xs = nodes.map(n => n.position.x);
    const ys = nodes.map(n => n.position.y);
    const minX = Math.min(...xs);
    const minY = Math.min(...ys);
    const maxX = Math.max(...xs) + 400;
    const maxY = Math.max(...ys) + 300;

    const el = canvasRef.current;
    if (!el) return;
    const w = el.clientWidth;
    const h = el.clientHeight;
    const scaleX = w / (maxX - minX + 120);
    const scaleY = h / (maxY - minY + 120);
    const zoom = Math.max(MIN_ZOOM, Math.min(MAX_ZOOM, Math.min(scaleX, scaleY) * 0.85));
    setTransform({
      x: (w - (maxX - minX) * zoom) / 2 - minX * zoom,
      y: (h - (maxY - minY) * zoom) / 2 - minY * zoom,
      zoom,
    });
  }, [nodes]);

  /* ── Zoom (mouse wheel) ── */
  useEffect(() => {
    const el = canvasRef.current;
    if (!el) return;

    const onWheel = (e) => {
      e.preventDefault();
      const factor = e.deltaY < 0 ? 1.1 : 0.9;
      setTransform(prev => {
        const newZoom = Math.max(MIN_ZOOM, Math.min(MAX_ZOOM, prev.zoom * factor));
        const rect = el.getBoundingClientRect();
        const mx = e.clientX - rect.left;
        const my = e.clientY - rect.top;
        const ratio = newZoom / prev.zoom;
        return {
          x: mx - (mx - prev.x) * ratio,
          y: my - (my - prev.y) * ratio,
          zoom: newZoom,
        };
      });
    };

    el.addEventListener('wheel', onWheel, { passive: false });
    return () => el.removeEventListener('wheel', onWheel);
  }, []);

  /* ── Edges ── */
  const renderEdges = () =>
    edges.map(edge => {
      const src = nodes.find(n => n.id === edge.source);
      const tgt = nodes.find(n => n.id === edge.target);
      if (!src || !tgt) return null;

      const srcSize = sizeOf(src);
      const tgtSize = sizeOf(tgt);

      const sx = src.position.x + srcSize.w;
      const sy = src.position.y + srcSize.h / 2;
      const tx = tgt.position.x;
      const ty = tgt.position.y + tgtSize.h / 2;
      const mx = (sx + tx) / 2;

      const color = edge.style?.stroke || 'var(--accent)';
      const dashed = edge.animated;

      return (
        <g key={edge.id}>
          <path
            d={`M${sx},${sy} C${mx},${sy} ${mx},${ty} ${tx},${ty}`}
            fill="none"
            stroke={color}
            strokeWidth="2"
            strokeDasharray={dashed ? '6 4' : undefined}
            strokeLinecap="round"
            opacity="0.75"
          />
          {/* arrowhead */}
          <path
            d={`M${tx - 8},${ty - 5} L${tx},${ty} L${tx - 8},${ty + 5}`}
            fill="none"
            stroke={color}
            strokeWidth="2"
            strokeLinecap="round"
            strokeLinejoin="round"
            opacity="0.75"
          />
        </g>
      );
    });

  /* ── Dot-grid background offset ── */
  const dotOffsetX = ((transform.x % 40) + 40) % 40;
  const dotOffsetY = ((transform.y % 40) + 40) % 40;

  return (
    <div
      ref={canvasRef}
      className={`infinite-canvas${isPanning ? ' is-panning' : ''}`}
      onMouseDown={onMouseDown}
      onMouseMove={onMouseMove}
      onMouseUp={onMouseUp}
      onMouseLeave={onMouseUp}
      onContextMenu={(e) => {
        // Kartın üstündeki sağ tık tarayıcının kendi menüsü olarak kalıyor:
        // yazıyı kopyalamak, bağlantıyı açmak orada çalışmaya devam etmeli.
        if (!onCreateCard || e.target.closest('.canvas-node')) return;
        e.preventDefault();

        const rect = canvasRef.current.getBoundingClientRect();
        const zoom = transformRef.current.zoom || 1;
        setMenu({
          // Menü ekranda tıklanan yerde duruyor…
          screenX: e.clientX - rect.left,
          screenY: e.clientY - rect.top,
          // …kart ise tuvalin o noktasına kuruluyor.
          x: Math.round((e.clientX - rect.left - transformRef.current.x) / zoom),
          y: Math.round((e.clientY - rect.top - transformRef.current.y) / zoom),
        });
      }}
    >
      {/* Dot-grid */}
      <svg
        className="canvas-grid"
        style={{ backgroundPosition: `${dotOffsetX}px ${dotOffsetY}px` }}
      />

      {/* Scalable layer */}
      <div
        className="canvas-viewport"
        style={{
          transform: `translate(${transform.x}px,${transform.y}px) scale(${transform.zoom})`,
          transformOrigin: '0 0',
        }}
      >
        {/* Edges SVG */}
        <svg
          className="canvas-edges-svg"
          width={SVG_SIZE}
          height={SVG_SIZE}
          style={{ position: 'absolute', top: 0, left: 0, overflow: 'visible', pointerEvents: 'none' }}
        >
          {renderEdges()}
        </svg>

        {/* Nodes */}
        {nodes.map(node => {
          const Comp = NODE_COMPONENTS[node.type];
          if (!Comp) return null;
          const isDragging = draggingId === node.id;
          return (
            <div
              key={node.id}
              ref={measureRef}
              data-node-id={node.id}
              className={`canvas-node${onNodeMove ? ' is-draggable' : ''}${isDragging ? ' is-dragging' : ''}`}
              style={{ left: node.position.x, top: node.position.y }}
              onMouseDown={(e) => startDrag(e, node)}
              onClick={(e) => {
                e.stopPropagation();
                // Sürükleme bittiğinde gelen tıklama düğümü seçmesin.
                if (dragRef.current?.id === node.id && dragRef.current.moved) {
                  dragRef.current = null;
                  return;
                }
                onNodeClick?.(node);
              }}
            >
              <Comp
                data={node.data}
                onAsk={onNodeAsk ? (text) => onNodeAsk(node, text) : undefined}
                onDelete={onNodeDelete ? () => onNodeDelete(node) : undefined}
                onConfirmMatch={onNodeConfirmMatch
                  ? (match, accepted) => onNodeConfirmMatch(node, match, accepted)
                  : undefined}
                onChartType={onNodeChartType
                  ? (type) => onNodeChartType(node, type)
                  : undefined}
              />
            </div>
          );
        })}
      </div>

      {/* Controls */}
      {menu && (
        <CanvasContextMenu
          x={menu.screenX}
          y={menu.screenY}
          onClose={() => setMenu(null)}
          onPick={(chartType) => {
            setMenu(null);
            onCreateCard(chartType, { x: menu.x, y: menu.y });
          }}
        />
      )}
      <div className="canvas-controls">
        <button className="canvas-ctrl-btn" title="Tümünü Göster" onClick={fitView}>⌖</button>
        <button className="canvas-ctrl-btn" title="Yakınlaş"
          onClick={() => setTransform(p => ({ ...p, zoom: Math.min(MAX_ZOOM, p.zoom * 1.2) }))}>+</button>
        <button className="canvas-ctrl-btn" title="Uzaklaş"
          onClick={() => setTransform(p => ({ ...p, zoom: Math.max(MIN_ZOOM, p.zoom * 0.8) }))}>−</button>
        <span className="canvas-zoom-label">{Math.round(transform.zoom * 100)}%</span>
      </div>

      {/* Empty state */}
      {nodes.length === 0 && (
        <div className="canvas-empty">
          <div className="canvas-empty-icon">
            <svg width="64" height="64" viewBox="0 0 64 64" fill="none">
              <rect x="8" y="16" width="20" height="32" rx="3" stroke="var(--accent)" strokeWidth="2" fill="var(--accent-soft)"/>
              <rect x="36" y="8" width="20" height="20" rx="3" stroke="var(--accent)" strokeWidth="2" fill="var(--accent-soft)"/>
              <rect x="36" y="36" width="20" height="20" rx="3" stroke="var(--accent)" strokeWidth="2" fill="var(--accent-soft)"/>
              <path d="M28 32h8" stroke="var(--accent)" strokeWidth="2" strokeLinecap="round"/>
              <path d="M28 32c0-8 8-8 8-16" stroke="var(--accent)" strokeWidth="2" strokeLinecap="round" strokeDasharray="4 3" fill="none"/>
            </svg>
          </div>
          <h3>Sonsuz Tuval</h3>
          {/* "Sol panelden sorun" yazıyordu; o panel kaldırıldı ve giriş
              artık yalnızca sağ tık. Yanlış yönlendiren bir boş ekran,
              yönlendirmeyen bir boş ekrandan kötü. */}
          <p>
            Tuvale <strong>sağ tıklayın</strong> ve bir grafik türü seçin.
            <br />Soru soracağınız kart oraya kurulur.
          </p>
          <div className="canvas-empty-hints">
            <span>🖱 Sürükle: Tuval kaydır</span>
            <span>⚲ Tekerlek: Yakınlaş / Uzaklaş</span>
            <span>✥ Düğümü tut: Yerini değiştir</span>
          </div>
        </div>
      )}
    </div>
  );
}
