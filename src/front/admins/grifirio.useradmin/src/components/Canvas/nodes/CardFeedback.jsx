import React, { useState } from 'react';
import { submitQueryFeedback } from '../../../services/dataAnalysisService';

/* Sonuca oy — "bu cevap işime yaradı mı".

   Sistemin kullanışlı olduğunu kanıtlamanın tek doğrudan yolu kullanıcıya
   sormak; diğer her sinyal (aynı soruyu tekrar sorma, netleştirme) dolaylı.
   Tek tık, zorunlu değil, fikir değiştirilebilir: son oy geçerli. */

export default function CardFeedback({ queryId, initialRating = null }) {
  const [rating, setRating] = useState(initialRating);
  const [busy, setBusy] = useState(false);
  const [failed, setFailed] = useState(false);

  if (!queryId) return null;

  const vote = async (value) => {
    if (busy || value === rating) return;
    const previous = rating;
    setRating(value);
    setBusy(true);
    setFailed(false);
    try {
      await submitQueryFeedback(queryId, value);
    } catch {
      // Oy kaydedilemediyse seçili görünmemeli: kullanıcı verdiğini sanır.
      setRating(previous);
      setFailed(true);
    } finally {
      setBusy(false);
    }
  };

  const button = (value, label, title) => (
    <button
      type="button"
      className={`bi-feedback-btn${rating === value ? ' is-active' : ''}`}
      title={title}
      aria-pressed={rating === value}
      disabled={busy}
      onMouseDown={(e) => e.stopPropagation()}
      onClick={(e) => { e.stopPropagation(); vote(value); }}
    >
      {label}
    </button>
  );

  return (
    <div className="bi-feedback">
      <span className="bi-feedback-label">
        {failed ? 'Oy kaydedilemedi, tekrar deneyin' : rating ? 'Teşekkürler' : 'Bu sonuç işinize yaradı mı?'}
      </span>
      {button(1, '👍', 'İşime yaradı')}
      {button(-1, '👎', 'Yanlış ya da yararsız')}
    </div>
  );
}
