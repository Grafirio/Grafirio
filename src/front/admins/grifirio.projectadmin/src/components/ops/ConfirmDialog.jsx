import { useEffect, useId, useRef } from 'react';

export default function ConfirmDialog({ title, message, confirmLabel, onConfirm, onCancel }) {
  const dialog = useRef(null);
  const previousFocus = useRef(document.activeElement);
  const titleId = useId();
  useEffect(() => {
    const element = dialog.current;
    const previous = previousFocus.current;
    element.showModal();
    return () => { element.close(); previous?.focus(); };
  }, []);
  return (
      <dialog ref={dialog}
        className="gf-dialog pa-confirm"
        role="alertdialog"
        aria-labelledby={titleId}
        onCancel={(event) => { event.preventDefault(); onCancel(); }}
      >
        <div className="gf-dialog__header">
          <h3 id={titleId}>{title}</h3>
        </div>
        <div className="gf-dialog__body">
          <p>{message}</p>
        </div>
        <div className="gf-dialog__footer">
          <button autoFocus className="gf-btn gf-btn--ghost" onClick={onCancel}>Vazgeç</button>
          <button className="gf-btn gf-btn--danger" onClick={onConfirm}>{confirmLabel}</button>
        </div>
      </dialog>
  );
}
