import React, { useEffect, useRef, useState } from 'react';
import BiChartNode from './BiChartNode';
import NodeComposer from './NodeComposer';
import CardAudit from './CardAudit';
import MatchConfirmations from './MatchConfirmations';
import { CHART_TYPES } from '../chartTypes';

/**
 * Analiz kartı — konuşma ve grafik TEK kümede.
 *
 * Eski yapıda her soru tuvale ayrı düğümler bırakıyordu: soru kutusu, cevap
 * kutusu, grafik, bazen uyarı kutusu. Üç soru sonra ekranda on iki kutu
 * oluyor, hangisinin hangisine ait olduğu okunmuyordu. Yan paneldeki sohbet
 * de bütün kartların sorularını tek akışta topladığı için ayrı konuların
 * cümleleri alt alta düşüyordu.
 *
 * Bu kart o iki sorunu birden kapatıyor: konuşma kendi grafiğinin YANINDA
 * duruyor ve oradan çıkmıyor. Yeni soru yeni kutu açmıyor — aynı kartın
 * grafiğini güncelliyor.
 *
 * Grafik türünü kullanıcı seçiyor ve seçimi modelinkini EZİYOR. Tür bir
 * görünüm tercihi, verinin kendisi değil; kullanıcı sonucu gördükten sonra
 * da değiştirebilmeli.
 */
export default function BiAnalysisCard({ data, onAsk, onDelete, onChartType, onConfirmMatch }) {
  const turns = data?.turns || [];
  const streamRef = useRef(null);
  const handleSubmit = (text) => onAsk?.(text);

  // Yeni tur geldiğinde konuşma dibe kayıyor: cevabı görmek için kaydırmak
  // gerekmemeli.
  useEffect(() => {
    streamRef.current?.scrollTo({ top: streamRef.current.scrollHeight, behavior: 'smooth' });
  }, [turns.length, data?.loading, data?.pendingConfirmations?.length]);

  return (
    <div className="bi-card">
      <div className="bi-card-head">
        <ChartTypePicker value={data?.chartType} onChange={onChartType} />
        {onDelete && (
          <button
            type="button"
            className="bi-card-close"
            title="Bu kartı sil"
            onMouseDown={(e) => e.stopPropagation()}
            onClick={(e) => { e.stopPropagation(); onDelete(); }}
          >
            ✕
          </button>
        )}
      </div>

      <div className="bi-card-body">
        <div className="bi-card-talk">
          <div className="bi-card-stream" ref={streamRef}>
            {turns.length === 0 && !data?.loading && (
              <p className="bi-card-hint">
                Ne görmek istediğinizi yazın. Cevap yandaki grafiğe gelir.
              </p>
            )}

            {turns.map((turn, i) => (
              <div
                key={i}
                className={`bi-turn bi-turn--${turn.role}${turn.error ? ' bi-turn--error' : ''}`}
              >
                {turn.content}
              </div>
            ))}

            {data?.loading && (
              <div className="bi-turn bi-turn--ai bi-turn--loading">
                <span /><span /><span />
              </div>
            )}
            <MatchConfirmations
              pending={data?.pendingConfirmations}
              states={data?.confirmationStates}
              busy={data?.confirming || data?.loading}
              needsClarification={data?.needsRelationshipClarification && !data?.confirmationRejected}
              onAnswer={onConfirmMatch}
            />
          </div>

          <CardAudit audit={data?.audit} />

          <NodeComposer
            placeholder={turns.length === 0
              ? 'Örn. en çok gelir getiren 5 firma'
              : 'Bu grafiği değiştir…'}
            busy={data?.loading || data?.confirming}
            onSubmit={handleSubmit}
          />
        </div>

        <div className="bi-card-chart">
          {data?.chart ? (
            <BiChartNode
              data={{
                ...data.chart,
                // Keep the source type available for validation before conversion.
                displayType: data.chartType,
                evidence: data.evidence,
              }}
            />
          ) : (
            <div className="bi-card-empty">
              {data?.loading ? 'Hesaplanıyor…' : 'Grafik burada görünecek'}
            </div>
          )}
        </div>
      </div>
    </div>
  );
}

/**
 * Tür seçici. Sağ tıkta bir kez seçiliyor ama sonradan da değişebilmeli:
 * hangi türün okunaklı olduğu çoğu zaman ancak veriyi gördükten sonra
 * belli oluyor ve bunun için soruyu yeniden sormak gerekmemeli.
 */
function ChartTypePicker({ value, onChange }) {
  const [open, setOpen] = useState(false);
  const current = CHART_TYPES.find(t => t.id === value);

  return (
    <div className="bi-card-types">
      <button
        type="button"
        className="bi-card-type-current"
        onMouseDown={(e) => e.stopPropagation()}
        onClick={(e) => { e.stopPropagation(); setOpen(o => !o); }}
      >
        {current?.label ?? 'Grafik türünü seçin'} <span className="bi-card-caret">▾</span>
      </button>

      {open && (
        <div className="bi-card-type-menu" onMouseDown={(e) => e.stopPropagation()}>
          {CHART_TYPES.map(type => (
            <button
              key={type.id}
              type="button"
              className={`bi-card-type-option${type.id === current?.id ? ' is-active' : ''}`}
              onClick={(e) => {
                e.stopPropagation();
                setOpen(false);
                onChange?.(type.id);
              }}
            >
              <span className="bi-card-type-label">{type.label}</span>
              <span className="bi-card-type-hint">{type.hint}</span>
            </button>
          ))}
        </div>
      )}
    </div>
  );
}
