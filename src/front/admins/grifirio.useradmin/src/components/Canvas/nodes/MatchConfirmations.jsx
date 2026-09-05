import getPendingConfirmations from '../../../utils/relationships/getPendingConfirmations';
import relationshipKey from '../../../utils/relationships/relationshipKey';
import MatchConfirmationItem from './MatchConfirmationItem';

export default function MatchConfirmations({ pending, states, busy, needsClarification, onAnswer }) {
  const matches = getPendingConfirmations({ pendingConfirmations: pending });
  if (!onAnswer || !matches.length) return null;
  const stopPropagation = (event) => event.stopPropagation();
  const renderMatch = (match) => (
    <MatchConfirmationItem key={relationshipKey(match)} match={match}
      states={states} busy={busy} onAnswer={onAnswer} />
  );

  return (
    <div className="bi-match-confirm" onMouseDown={stopPropagation} onClick={stopPropagation}>
      <div className="bi-match-confirm-title">Önerilen ilişkileri doğrulayın</div>
      <p className="bi-match-confirm-text">
        {needsClarification
          ? 'Tüm ilişkiler onaylanıp aktif sözlüğe uygulandığında özgün sorunuz otomatik sürdürülecek.'
          : 'Onaylanan ilişkiler hemen aktif sözlüğe uygulanır. Mevcut grafik değişmez.'}
      </p>
      <p className="bi-match-confirm-text">
        {matches.length > 1
          ? 'Tek tek düğmeleri kullanın. Tümü için “hepsini onaylıyorum” veya “hepsini reddediyorum” yazın; “evet” / “hayır” tümünü seçmez.'
          : 'Düğmeleri kullanabilir veya sohbete “evet” / “onaylıyorum”, “hayır” / “reddediyorum” yazabilirsiniz.'}
      </p>
      {matches.map(renderMatch)}
    </div>
  );
}