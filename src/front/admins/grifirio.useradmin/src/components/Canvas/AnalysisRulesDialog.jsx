import React, { useEffect, useState } from 'react';
import { createPortal } from 'react-dom';
import { IconShieldCheck, IconCalendar, IconTimeline, IconInfoCircle } from '@tabler/icons-react';
import { getAnalysisPolicy } from '../../services/dataAnalysisService';
import './AnalysisPresentation.css';

export default function AnalysisRulesDialog({ onClose }) {
  const [policy, setPolicy] = useState(null);
  const [error, setError] = useState(false);
  useEffect(() => {
    let live = true;
    getAnalysisPolicy().then(data => { if (live) setPolicy(data); }).catch(() => { if (live) setError(true); });
    return () => { live = false; };
  }, []);
  return createPortal(<div className="analysis-modal-backdrop"><section className="analysis-modal analysis-rules-dialog" role="dialog" aria-modal="true" aria-label="Analiz kuralları">
    <div className="presentation-heading"><span className="presentation-heading-icon"><IconShieldCheck size={25} stroke={1.5} /></span><div><h2>Analiz kuralları</h2><p>Tüm analizlerde uygulanan ortak kurallar.</p></div></div>
    <p>Burada tablo veya alan seçmenize gerek yok. Görmek istediğiniz sonucu kendi cümlelerinizle sorun; Grafirio hesaplamayı bu kurallarla kontrol eder.</p>
    {!policy && !error && <p role="status">Kurallar yükleniyor…</p>}
    {error && <p role="alert">Kurallar şu anda okunamadı. Ekranı kapatıp tekrar açın.</p>}
    {policy && <div className="analysis-rules-grid">
      <section><IconCalendar size={22} /><h3>En az {policy.minHistoryPeriods} tamamlanmış ay</h3><p>İçinde bulunulan ay ve tarih filtresinde kısmi kalan aylar eğitim geçmişine alınmaz. Kullanılan dönem sonucun altında açıklanır.</p></section>
      <section><IconTimeline size={22} /><h3>En fazla {policy.maxHorizon} ay ileri tahmin</h3><p>Daha uzun bir süre isterseniz {policy.maxHorizon} aylık tahmin gösterilir. Uygulanan sınır sonucun altında yeşil bir notla belirtilir.</p></section>
      <section><IconShieldCheck size={22} /><h3>Eksik aylar sıfır sayılmaz</h3><p>Veri eksikse tahmin durdurulur. Eksik dönemleri içermeyen bir tarih aralığı seçebilir veya veri aktarımını kontrol edebilirsiniz.</p></section>
      <section><IconInfoCircle size={22} /><h3>Her sonucun açıklaması var</h3><p>Hangi verinin kullanıldığını, nasıl hesaplandığını ve belirsizlikleri sonucun altında görürsünüz. Bu açıklamalar PDF sunusuna da eklenir.</p></section>
    </div>}
    <div className="analysis-rules-example"><strong>Örnek soru</strong><p>“Aylık satışlarımızı göster ve önümüzdeki 5 yıl için tahmin yap.”</p><p>Yeterli geçmiş varsa {policy?.maxHorizon ?? '12'} aylık tahmin hazırlanır ve süre sınırı açıklanır. Tahminler öngörüdür; kesin sonuç değildir.</p></div>
    <p className="analysis-rules-footnote">Kimlik ve kayıt numaraları tutar olarak toplanmaz. Birim bilinmiyorsa varsayılmaz. Alan bazlı özelleştirme bu aşamada kapalıdır.</p>
    <div className="analysis-modal-actions"><button className="analysis-button-primary" onClick={onClose}>Anladım</button></div>
  </section></div>, document.body);
}
