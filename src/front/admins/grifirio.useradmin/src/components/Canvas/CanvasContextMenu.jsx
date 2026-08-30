import React, { useEffect, useLayoutEffect, useRef, useState } from 'react';
import { CHART_TYPES } from './chartTypes';

/* Menünün kenardan taşmaması için bırakılan boşluk. */
const EDGE_GAP = 8;

/**
 * Tuvale sağ tıklayınca çıkan menü: yeni analiz kartı, seçilen grafik
 * türüyle.
 *
 * Tür ÖNCE soruluyor, sonra soru yazılıyor. Sebebi, sorunun kendisinin
 * çoğu zaman türü belirlememesi: "en çok gelir getiren 5 firma" çubuk da
 * pasta da olabilir ve modelin tahminini sonradan düzeltmek için soruyu
 * yeniden sormak gerekiyordu.
 *
 * Menü tıklandığı YERDE açılıyor ve kart da oraya kuruluyor: kullanıcının
 * seçtiği boşluk, kartın yeri demek. Eskiden sonuçlar sıradaki boş satıra
 * kendiliğinden diziliyordu ve nereye çıkacağı önceden bilinemiyordu.
 */
export default function CanvasContextMenu({ x, y, onPick, onClose }) {
  const ref = useRef(null);

  /* Menü tıklanan yerde açılıyor ama tuvalin dışına taşmamalı: sağ alt
     köşeye tıklandığında liste ekranın altında kalıyor ve seçenekler
     görünmüyordu. Ölçüm çizimden SONRA yapılıyor (yükseklik tür sayısına
     bağlı), o yüzden layout effect — kullanıcı menünün yerinden zıpladığını
     görmemeli. */
  const [placed, setPlaced] = useState({ left: x, top: y });

  useLayoutEffect(() => {
    const menu = ref.current;
    const host = menu?.offsetParent;
    if (!menu || !host) return;

    setPlaced({
      left: Math.max(EDGE_GAP, Math.min(x, host.clientWidth - menu.offsetWidth - EDGE_GAP)),
      top: Math.max(EDGE_GAP, Math.min(y, host.clientHeight - menu.offsetHeight - EDGE_GAP)),
    });
  }, [x, y]);

  useEffect(() => {
    const dismiss = (e) => {
      if (!ref.current?.contains(e.target)) onClose();
    };
    const onKey = (e) => { if (e.key === 'Escape') onClose(); };

    // `capture`: tuvalin kendi mousedown'ı kaydırmayı başlatıyor; menü ondan
    // önce kapanmalı, yoksa tıklama hem menüyü kapatıp hem tuvali kaydırır.
    document.addEventListener('mousedown', dismiss, true);
    document.addEventListener('keydown', onKey);
    return () => {
      document.removeEventListener('mousedown', dismiss, true);
      document.removeEventListener('keydown', onKey);
    };
  }, [onClose]);

  return (
    <div
      ref={ref}
      className="canvas-menu"
      style={{ left: placed.left, top: placed.top }}
      onContextMenu={(e) => e.preventDefault()}
    >
      <div className="canvas-menu-title">Yeni analiz</div>
      {CHART_TYPES.map(type => (
        <button
          key={type.id}
          type="button"
          className="canvas-menu-item"
          onClick={() => onPick(type.id)}
        >
          <span className="canvas-menu-label">{type.label}</span>
          <span className="canvas-menu-hint">{type.hint}</span>
        </button>
      ))}
    </div>
  );
}
