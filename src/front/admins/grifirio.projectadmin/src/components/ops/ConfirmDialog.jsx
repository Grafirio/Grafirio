export default function ConfirmDialog({ title, message, confirmLabel, onConfirm, onCancel }) {
  return (
    <div className="gf-overlay" role="presentation" onClick={onCancel}>
      <div
        className="gf-dialog"
        role="alertdialog"
        aria-modal="true"
        aria-labelledby="ops-confirm-title"
        onClick={(event) => event.stopPropagation()}
      >
        <div className="gf-dialog__header">
          <h3 id="ops-confirm-title">{title}</h3>
        </div>
        <div className="gf-dialog__body">
          <p>{message}</p>
        </div>
        <div className="gf-dialog__footer">
          <button className="gf-btn gf-btn--ghost" onClick={onCancel}>Vazgeç</button>
          <button className="gf-btn gf-btn--danger" onClick={onConfirm}>{confirmLabel}</button>
        </div>
      </div>
    </div>
  );
}
