import React from 'react';
import formatReportNumber from '../../../utils/formatReportNumber';

export default function ForecastSummary({ report }) {
  const summary = report?.forecastSummary;
  if (!summary) return null;
  return <section className="forecast-summary">
    <h3>Öngörü özeti</h3>
    <p>{summary.period.from} – {summary.period.to} · {summary.horizon} aylık öngörü · {report.unit === 'Belirtilmedi' ? 'Birim henüz doğrulanmadı' : report.unit}</p>
    <div className="forecast-summary-grid">
      <div className="forecast-summary-main"><span>{summary.totalLabel || 'Son dönem öngörüsü'}</span><strong>{formatReportNumber(summary.total ?? summary.lastValue)}</strong></div>
      {summary.previousTotal != null && <div><span>{summary.previousLabel}</span><strong>{formatReportNumber(summary.previousTotal)}</strong>
        {summary.changePercent != null && <small>Bu toplamla fark: {summary.changePercent > 0 ? '+' : ''}{summary.changePercent.toLocaleString('tr-TR', { maximumFractionDigits: 1 })}% — model başarı oranı değildir.</small>}
      </div>}
      {summary.previousTotal == null && report.legacyCalculation && <div><span>Geçmişle karşılaştırma</span><strong>Yeniden analiz gerekli</strong><small>Kısmi aylar kontrol edilmeden büyüme veya azalış oranı gösterilmez.</small></div>}
      <div><span>En yüksek aylık öngörü</span><strong>{formatReportNumber(summary.peak.value)}</strong><small>{summary.peak.period}</small></div>
    </div>
    <p className="forecast-summary-caution">Bu değerler öngörüdür. Geçmiş dönemlerde tahmin başarısı henüz ölçülmemiştir.</p>
    {report.legacyCalculation && <p className="forecast-summary-caution">Eski hesaplama: bu kayıt tam ay kontrolünden önce üretildi. Güncel tahmin için analizi yeniden çalıştırın.</p>}
    {report.businessQuestions?.length > 0 && <div className="forecast-business-context"><strong>Netleştirilmesi gereken bilgiler</strong><ul>{report.businessQuestions.map((question, i) => <li key={i}>{question}</li>)}</ul></div>}
  </section>;
}
