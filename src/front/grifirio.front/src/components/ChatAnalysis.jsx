import GMark from './GMark';

const points = [
  'Türkçe soru–cevap, kaynak kolon referanslarıyla',
  'Her grafiğin altında tek cümlelik özet',
  'Anormal değerleri kendiliğinden işaretler',
];

const margins = [
  { name: 'Deniz Gıda', pct: 22, value: '4,1%', color: '#D64550' },
  { name: 'Batı Toptan', pct: 34, value: '6,4%', color: '#E4633C' },
  { name: 'Ay Market', pct: 46, value: '8,8%', color: '#F0902B' },
];

export default function ChatAnalysis() {
  return (
    <section className="band" id="analiz">
      <div className="wrap chat-inner">
        <div>
          <p className="mono eyebrow" style={{ color: '#8A2E8E' }}>
            Sohbet ederek analiz
          </p>
          <h2 className="section-title">Grafiği kurmak değil, soru sormak yeter</h2>
          <p className="section-lead">
            Pivot tablo, formül, eksen ayarı yok. “Hangi müşteri grubu kâr marjını düşürüyor?”
            diye sor; Grafirio doğru grafiği seçip kanvasa ekler.
          </p>

          <ul className="check-list">
            {points.map((p) => (
              <li key={p}>
                <span className="check" aria-hidden="true">
                  ✓
                </span>
                {p}
              </li>
            ))}
          </ul>
        </div>

        <div className="chat" aria-hidden="true">
          <div className="chat-user">Kâr marjı en düşük 5 müşteriyi göster</div>

          <div className="chat-reply">
            <span className="chat-mark">
              <GMark compact />
            </span>
            <div className="chat-bubble">
              <p>
                5 müşteriyi sıraladım. <strong>Deniz Gıda</strong> marjı %4,1 ile en düşük —
                iskonto oranı ortalamanın 2,3 katı.
              </p>
              <ul className="chat-bars">
                {margins.map((m) => (
                  <li key={m.name}>
                    <span className="chat-bar-name">{m.name}</span>
                    <span className="chat-track">
                      <span
                        className="chat-fill"
                        style={{ '--w': `${m.pct}%`, background: m.color }}
                      />
                    </span>
                    <span className="mono chat-bar-value">{m.value}</span>
                  </li>
                ))}
              </ul>
            </div>
          </div>

          <div className="chat-input">
            <span>Bir soru yaz…</span>
            <span className="chat-send">↑</span>
          </div>
        </div>
      </div>
    </section>
  );
}
