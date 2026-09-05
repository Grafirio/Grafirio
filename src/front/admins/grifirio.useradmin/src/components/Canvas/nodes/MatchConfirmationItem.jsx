import relationshipKey from '../../../utils/relationships/relationshipKey';

export default function MatchConfirmationItem({ match, states, busy, onAnswer }) {
  const state = states?.[relationshipKey(match)];
  const handleAccept = () => onAnswer(match, true);
  const handleReject = () => onAnswer(match, false);

  return (
    <div className="bi-match-confirm-item">
      <div className="bi-match-confirm-pair">
        <span>{match.fromTable}.{match.fromColumn}</span>
        <span>→ {match.toTable}.{match.toColumn}</span>
      </div>
      {state?.error && <p className="bi-turn--error" role="alert">{state.error}</p>}
      <div className="bi-match-confirm-actions">
        <button type="button" className="bi-match-btn bi-match-btn-yes"
          disabled={busy || state?.busy} onClick={handleAccept}>
          {state?.busy ? 'Uygulanıyor…' : state?.error ? 'Tekrar onayla' : 'Onaylıyorum'}
        </button>
        <button type="button" className="bi-match-btn bi-match-btn-no"
          disabled={busy || state?.busy} onClick={handleReject}>
          {state?.error ? 'Tekrar reddet' : 'Reddediyorum'}
        </button>
      </div>
    </div>
  );
}