import React, { useState } from 'react';

/* ─────────────────────────────────────────────────────────────
   Silme onayı

   Silinen bir grafik geri gelmiyor: tuvalden düşüyor ve yerleşim kaydına
   siliniş olarak yazılıyor. Bu yüzden onay tek tıkla geçilebilir olmamalı —
   "Silebilirsin" yazmak, yanlışlıkla basılan bir düğmenin yapamayacağı
   bir şey.
───────────────────────────────────────────────────────────── */
export const DELETE_PHRASE = 'Silebilirsin';

export default function DeleteConfirmDialog({ node, onCancel, onConfirm }) {
  const [typed, setTyped] = useState('');
  const confirmable = typed.trim() === DELETE_PHRASE;

  const isQuestion = node?.data?.type === 'question';
  const label = isQuestion
    ? 'Bu soru ve ondan çıkan bütün sonuçlar'
    : (node?.data?.title || 'Bu düğüm');

  return (
    <div className="cp-modal-backdrop" onMouseDown={onCancel}>
      <div
        className="cp-modal"
        role="dialog"
        aria-modal="true"
        aria-labelledby="cp-delete-title"
        onMouseDown={(e) => e.stopPropagation()}
      >
        <h3 id="cp-delete-title" className="cp-modal-title">Silmek istediğinize emin misiniz?</h3>

        <p className="cp-modal-body">
          <strong>{label}</strong> tuvalden kaldırılacak.
          {isQuestion && ' Sorunun cevabı, grafikleri ve üzerine sorulmuş takip soruları da gider.'}
          {' '}Bu işlem geri alınamaz.
        </p>

        <label className="cp-modal-label" htmlFor="cp-delete-phrase">
          Onaylamak için <code>{DELETE_PHRASE}</code> yazın:
        </label>
        <input
          id="cp-delete-phrase"
          className="cp-modal-input"
          value={typed}
          autoFocus
          autoComplete="off"
          placeholder={DELETE_PHRASE}
          onChange={(e) => setTyped(e.target.value)}
          onKeyDown={(e) => {
            if (e.key === 'Enter' && confirmable) onConfirm();
            if (e.key === 'Escape') onCancel();
          }}
        />

        <div className="cp-modal-actions">
          <button type="button" className="cp-modal-btn" onClick={onCancel}>Vazgeç</button>
          <button
            type="button"
            className="cp-modal-btn cp-modal-btn--danger"
            disabled={!confirmable}
            onClick={onConfirm}
          >
            Sil
          </button>
        </div>
      </div>
    </div>
  );
}
