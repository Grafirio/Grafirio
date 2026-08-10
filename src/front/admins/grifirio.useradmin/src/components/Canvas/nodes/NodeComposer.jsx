import React, { useState } from 'react';

/**
 * Düğümün üzerindeki küçük soru kutusu.
 *
 * Sol paneldeki sohbetten farkı bağlamı: buradan sorulan soru, üzerinde
 * durduğu düğümün devamı sayılır. Sol panelden sorulan her soru bağımsız
 * bir dal açar; ilişki kurmanın yolu buradan sormaktır.
 *
 * `onMouseDown`/`onClick` yayılımı burada durduruluyor — tuval bu olayları
 * sürükleme ve kaydırma olarak okuyor, yazarken düğümün kaçmaması gerek.
 */
export default function NodeComposer({
  placeholder = 'Bu sonucun üzerinden sor…',
  busy = false,
  autoFocus = false,
  onSubmit,
  onCancel,
}) {
  const [text, setText] = useState('');

  const submit = () => {
    const trimmed = text.trim();
    if (!trimmed || busy) return;
    setText('');
    onSubmit?.(trimmed);
  };

  return (
    <div
      className="bi-node-composer"
      onMouseDown={(e) => e.stopPropagation()}
      onClick={(e) => e.stopPropagation()}
    >
      <textarea
        rows={1}
        className="bi-node-composer-input"
        placeholder={placeholder}
        value={text}
        autoFocus={autoFocus}
        disabled={busy}
        onChange={(e) => {
          setText(e.target.value);
          e.target.style.height = 'auto';
          e.target.style.height = `${Math.min(e.target.scrollHeight, 96)}px`;
        }}
        onKeyDown={(e) => {
          if (e.key === 'Enter' && !e.shiftKey) {
            e.preventDefault();
            submit();
          }
          if (e.key === 'Escape') onCancel?.();
        }}
      />
      <button
        type="button"
        className="bi-node-composer-send"
        title="Gönder"
        disabled={!text.trim() || busy}
        onClick={submit}
      >
        {busy ? '…' : '↑'}
      </button>
    </div>
  );
}
