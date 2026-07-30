const steps = [
  {
    n: '01',
    color: '#0E8F8C',
    title: 'Bağla',
    body: 'Excel, CSV ya da PostgreSQL / MySQL bağlantısı. Dosyayı sürükle, gerisi otomatik eşleşir.',
  },
  {
    n: '02',
    color: '#8A2E8E',
    title: 'Yorumla',
    body: 'Yapay zekâ kolonları anlar, anlamlı kırılımları bulur ve neyin önemli olduğunu cümlelerle anlatır.',
  },
  {
    n: '03',
    color: '#F0902B',
    title: 'Kanvasta düzenle',
    body: "Widget'ları serbest kanvasta taşı, yeniden boyutlandır, paylaş. Bağlantı canlı kalır.",
  },
];

export default function HowItWorks() {
  return (
    <section className="section" id="nasil-calisir">
      <div className="wrap">
        <div className="section-head">
          <p className="mono eyebrow" style={{ color: '#0E8F8C' }}>
            Nasıl çalışır
          </p>
          <h2 className="section-title">Üç adımda dashboard</h2>
        </div>

        <div className="step-grid">
          {steps.map((s) => (
            <article className="step-card" style={{ '--accent': s.color }} key={s.n}>
              <span className="mono step-n">{s.n}</span>
              <h3>{s.title}</h3>
              <p>{s.body}</p>
            </article>
          ))}
        </div>
      </div>
    </section>
  );
}
