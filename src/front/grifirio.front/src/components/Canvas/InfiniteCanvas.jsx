import React, { useState, useRef, useCallback, useEffect } from 'react';
import BiChartNode from './nodes/BiChartNode';
import BiTableNode from './nodes/BiTableNode';
import BiInsightNode from './nodes/BiInsightNode';
import BiMetricNode from './nodes/BiMetricNode';
import './Canvas.css';

const NODE_COMPONENTS = {
  biChartNode: BiChartNode,
  biTableNode: BiTableNode,
  biInsightNode: BiInsightNode,
  biMetricNode: BiMetricNode,
};

const SVG_SIZE = 20000;
const MIN_ZOOM = 0.15;
const MAX_ZOOM = 3;

export default function InfiniteCanvas({ nodes = [], edges = [], onNodeClick }) {
  const [transform, setTransform] = useState({ x: 60, y: 60, zoom: 1 });
  const [isPanning, setIsPanning] = useState(false);
  const startPanRef = useRef({ x: 0, y: 0 });
  const canvasRef = useRef(null);

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

  /* ── Edges ── */
  const renderEdges = () =>
    edges.map(edge => {
      const src = nodes.find(n => n.id === edge.source);
      const tgt = nodes.find(n => n.id === edge.target);
      if (!src || !tgt) return null;

      const srcW = src.width || 380;
      const srcH = src.height || 160;
      const tgtH = tgt.height || 160;

      const sx = src.position.x + srcW;
      const sy = src.position.y + srcH / 2;
      const tx = tgt.position.x;
      const ty = tgt.position.y + tgtH / 2;
      const mx = (sx + tx) / 2;

      const color = edge.style?.stroke || '#7c3aed';
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
          return (
            <div
              key={node.id}
              className="canvas-node"
              style={{ left: node.position.x, top: node.position.y }}
              onClick={(e) => { e.stopPropagation(); onNodeClick?.(node); }}
            >
              <Comp data={node.data} />
            </div>
          );
        })}
      </div>

      {/* Controls */}
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
              <rect x="8" y="16" width="20" height="32" rx="3" stroke="#7c3aed" strokeWidth="2" fill="#f5f3ff"/>
              <rect x="36" y="8" width="20" height="20" rx="3" stroke="#7c3aed" strokeWidth="2" fill="#f5f3ff"/>
              <rect x="36" y="36" width="20" height="20" rx="3" stroke="#7c3aed" strokeWidth="2" fill="#f5f3ff"/>
              <path d="M28 32h8" stroke="#7c3aed" strokeWidth="2" strokeLinecap="round"/>
              <path d="M28 32c0-8 8-8 8-16" stroke="#7c3aed" strokeWidth="2" strokeLinecap="round" strokeDasharray="4 3" fill="none"/>
            </svg>
          </div>
          <h3>Sonsuz Tuval</h3>
          <p>Sol panelden bir rapor seçin veya soru sorun.<br />Analiz sonuçları buraya <strong>node</strong> olarak yerleştirilecek.</p>
          <div className="canvas-empty-hints">
            <span>🖱 Sürükle: Tuval kaydır</span>
            <span>⚲ Tekerlek: Yakınlaş / Uzaklaş</span>
          </div>
        </div>
      )}
    </div>
  );
}
