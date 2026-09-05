import { useAnalysis } from '../../contexts/AnalysisContext';
import selectAnalysisAnswers from '../../utils/analysis/selectAnalysisAnswers.js';
// Modal ve bildirim sinifları bu sayfa stilinde tanımlı; tablo seçim modalı
// da aynı `modal-*` sınıflarını kullandığı için dosya bölünmedi. Vite tüm
// CSS'i tek pakete derlediği için buradan içe aktarmanın ek maliyeti yok.
import '../../styles/SqlConnectionSettings.css';

/**
 * Analiz modalı ve küçültülmüş ilerleme bildirimi.
 *
 * Rota ağacının dışında, sağlayıcının yanında duruyor: kullanıcı sayfa
 * değiştirse de bildirim ekranda kalıyor ve iş bitince modal geri açılıyor.
 * Önceden ikisi de SqlConnectionSettings'in içindeydi ve o sayfadan
 * ayrılınca kayboluyorlardı.
 */
export default function AnalysisTracker() {
  const {
    analysis, run, resumeTracking, submit,
    minimize, restore, dismiss, hide, setConsent, setAnswer,
  } = useAnalysis();

  if (!analysis.open) return null;

    const hasAnswers = Object.keys(selectAnalysisAnswers(analysis.questions, analysis.answers)).length > 0;
  const hasQuestions = analysis.questions.length > 0 && analysis.status !== 'ready';

  if (analysis.minimized) {
    return (
      <div className="analysis-toast" role="status" aria-live="polite">
        <button className="analysis-toast__body" onClick={restore} title="Ayrıntıları göster">
          <span className="analysis-toast__icon">
            {analysis.running && <span className="gf-spinner"></span>}
            {/* Takip bırakıldıysa tik göstermek yalan olurdu: iş bitmedi,
                biz izlemeyi bıraktık. */}
            {!analysis.running && analysis.trackingAbandoned && <i className="ti ti-clock"></i>}
            {!analysis.running && !analysis.trackingAbandoned && (
              <i className={analysis.status === 'ready' ? 'ti ti-check' : 'ti ti-help-circle'}></i>
            )}
          </span>
          <span className="analysis-toast__text">
            <strong>{analysis.connectionName}</strong>
            <span>
              {analysis.running
                // Sunucu ilerlemeyi özete yazıyor ("3/17 parça tamamlandı").
                // Büyük şemada iş çeyrek saat sürebiliyor; sabit bir cümle
                // izleyen kullanıcı sistemin kilitlendiğini sanıyor.
                ? (analysis.summary || 'Tablolar okunuyor ve anlamlandırılıyor…')
                : analysis.trackingAbandoned
                  ? 'Takip bırakıldı — arka planda sürüyor olabilir'
                  : analysis.status === 'awaiting_answers' ? 'Yanıtlarınızı bekliyor'
                    : analysis.status === 'ready' ? 'Analiz tamamlandı' : 'Analiz durumunu kontrol edin'}
            </span>
          </span>
        </button>
        <button
          className="analysis-toast__close"
          title="Bildirimi gizle — analiz arka planda devam eder"
          onClick={hide}
        >
          <i className="ti ti-x"></i>
        </button>
      </div>
    );
  }

  return (
    <div className="modal-overlay" onClick={dismiss}>
      <div className="modal-container" onClick={(e) => e.stopPropagation()}>
        <div className="modal-header">
          <h2><i className="ti ti-sparkles"></i> Analiz — {analysis.connectionName}</h2>
          <div style={{ display: 'flex', gap: 4 }}>
            {analysis.running && (
              <button
                className="modal-close"
                title="Küçült — analiz arka planda devam eder"
                onClick={minimize}
              >
                <i className="ti ti-minus"></i>
              </button>
            )}
            {/* Analiz sürerken X de kapatmıyor, küçültüyor: işi durduramıyoruz,
                kapatmak yalnızca takibi kaybettirir. */}
            <button
              className="modal-close"
              title={analysis.running ? 'Küçült — analiz arka planda devam eder' : 'Kapat'}
              onClick={dismiss}
            >
              <i className="ti ti-x"></i>
            </button>
          </div>
        </div>

        <div className="modal-body">
          {analysis.error && (
            <div className="gf-alert gf-alert--danger" style={{ marginBottom: 16 }}>
              <i className="ti ti-alert-circle"></i> {analysis.error}
            </div>
          )}

          {analysis.status === 'ready' && (
            <div className="gf-alert gf-alert--success" style={{ marginBottom: 16 }}>
              <i className="ti ti-check"></i> Bu bağlantı hazır — “AI Sorgulama”dan soru sorabilirsiniz.
            </div>
          )}

          {analysis.status === 'awaiting_answers' && !hasQuestions && !analysis.running && (
            <div className="gf-alert" role="status">
              Sunucu yanıt bekliyor ancak soru listesi alınamadı.
              <button className="gf-btn" onClick={resumeTracking}>Soruları yenile</button>
            </div>
          )}

          {/* Takip bırakıldı: hata değil, bilgi. İşi durduramıyoruz ve
              durdurmadık; yalnızca izlemeyi bıraktık. Bu yüzden burada
              "yeniden başlat" değil "durumu yenile" var — yeni bir analiz
              başlatmak, süren işin üstüne ikinci bir iş koymak olurdu. */}
          {analysis.trackingAbandoned && (
            <div className="gf-alert" style={{ marginBottom: 16 }}>
              <i className="ti ti-clock"></i> Analiz uzun sürdü ve takip bırakıldı.
              İş sunucuda devam ediyor olabilir; durdurulmadı.
              <div style={{ marginTop: 12 }}>
                <button className="gf-btn" onClick={resumeTracking}>
                  <i className="ti ti-refresh"></i> Durumu yenile
                </button>
              </div>
            </div>
          )}

          {!analysis.running && !analysis.trackingAbandoned
            && analysis.status !== 'ready' && analysis.status !== 'awaiting_answers' && !hasQuestions && (
            <>
              <p className="gf-hint" style={{ marginBottom: 16 }}>
                Seçili tabloların yapısı okunacak, kolonların ne anlama geldiği çıkarılacak.
                Böylece soru sorarken kolon adı bilmeniz gerekmez.
              </p>

              <label className="gf-checkbox" style={{ marginBottom: 16, alignItems: 'flex-start' }}>
                <input
                  type="checkbox"
                  checked={analysis.consent}
                  onChange={(e) => setConsent(e.target.checked)}
                />
                <span>
                  Serbest metin kolonlarından (firma adı, ürün adı gibi) örnek değer okunmasına
                  izin veriyorum. <strong>Kimlik no, telefon, e-posta ve adres hiçbir koşulda
                  okunmaz.</strong> İzin vermezseniz eşleştirme yalnızca kolon adı ve tipe
                  dayanır, doğruluk düşebilir.
                </span>
              </label>

              <button className="gf-btn gf-btn--primary" onClick={run} disabled={!analysis.connectionId}>
                <i className="ti ti-player-play"></i> Analizi başlat
              </button>
            </>
          )}

          {analysis.running && (
            <div className="analysis-loading">
              <div className="spinner-large"></div>
              {/* Büyük şemada sözlük parçalara bölünüp ayrı ayrı üretiliyor
                  ve iş çeyrek saati bulabiliyor. Sunucu kaçıncı parçada
                  olduğunu özete yazıyor; sabit bir cümle göstermek, süreyi
                  olduğundan uzun hissettiriyor. */}
              <p>{analysis.summary || 'Tablolar okunuyor ve anlamlandırılıyor…'}</p>
              <p className="gf-hint">
                Geniş şemalarda bu işlem on beş dakikayı bulabilir. Bu pencereyi
                küçültüp başka sayfalara geçebilirsiniz; analiz arka planda sürer.
              </p>
            </div>
          )}

          {analysis.stats && (
            <div className="gf-alert" style={{ marginBottom: 16 }}>
              {analysis.stats.tables} tablo · {analysis.stats.columns} kolon ·
              {' '}{analysis.stats.sampled} kolondan örnek değer okundu
            </div>
          )}

          {/* Sürerken özet ilerlemeyi taşıyor ve yukarıda gösteriliyor;
              burada ikinci kez yazmak aynı satırı iki yerde tekrar ederdi. */}
          {analysis.summary && !analysis.running && (
            <p className="gf-hint" style={{ marginBottom: 16 }}>{analysis.summary}</p>
          )}

          {hasQuestions && (
            <>
              <h3 style={{ marginBottom: 12 }}>Birkaç şeyden emin olamadım</h3>
              <p className="gf-hint" style={{ marginBottom: 16 }}>
                Bildiklerinizi yanıtlayıp kaydedebilirsiniz; tüm soruları yanıtlamanız gerekmez.
                Kalan sorular sunucunun döndürdüğü duruma göre gösterilir.
              </p>

              {analysis.questions.map((q) => (
                <div key={q.id} className="analysis-section" style={{ marginBottom: 16 }}>
                  {(q.column || q.table) && (
                    <div className="gf-badge" style={{ marginBottom: 6 }}>
                      {q.column
                        ? (q.table ? `${q.table}.${q.column}` : q.column)
                        : q.table}
                    </div>
                  )}
                  <p style={{ marginBottom: 8 }}>{q.question}</p>
                  <div style={{ display: 'flex', gap: 8, flexWrap: 'wrap' }}>
                    {(q.options?.length ? q.options : ['Evet', 'Hayır', 'Emin değilim']).map((opt) => (
                      <button
                        key={opt}
                        className={`gf-btn gf-btn--sm ${analysis.answers[q.id] === opt ? 'gf-btn--primary' : ''}`}
                        onClick={() => setAnswer(q.id, opt)}
                        disabled={analysis.running}
                      >
                        {opt}
                      </button>
                    ))}
                  </div>
                </div>
              ))}
            </>
          )}
        </div>

        <div className="modal-footer">
          {analysis.running ? (
            <button className="gf-btn gf-btn--ghost" onClick={minimize}>
              <i className="ti ti-arrow-down-right"></i> Arka planda çalıştır
            </button>
          ) : (
            <button className="gf-btn gf-btn--ghost" onClick={hide}>
              Kapat
            </button>
          )}
          {hasQuestions && (
            <button
              className="gf-btn gf-btn--primary"
              onClick={submit}
              disabled={analysis.running || !hasAnswers}
            >
              <i className="ti ti-check"></i> Seçilen yanıtları kaydet
            </button>
          )}
        </div>
      </div>
    </div>
  );
}
