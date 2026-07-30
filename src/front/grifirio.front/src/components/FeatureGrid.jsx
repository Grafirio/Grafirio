const features = [
  {
    color: '#0E8F8C',
    title: 'Serbest kanvas',
    body: 'Izgaraya sıkışma. Widget’ları istediğin yere koy.',
  },
  {
    color: '#4B4A9F',
    title: 'Canlı bağlantı',
    body: 'Kaynak güncellenince dashboard da güncellenir.',
  },
  {
    color: '#C42E6E',
    title: 'Paylaşılabilir link',
    body: 'Ekibe tek link; isteyene salt-okunur erişim.',
  },
  {
    color: '#F0902B',
    title: 'Veri güvenliği',
    body: 'Verin AB sunucularında, eğitimde kullanılmaz.',
  },
];

export default function FeatureGrid() {
  return (
    <section className="section" id="ozellikler">
      <div className="wrap">
        <div className="feature-grid">
          {features.map((f) => (
            <article className="feature-cell" key={f.title}>
              <span className="feature-rule" style={{ background: f.color }} />
              <h3>{f.title}</h3>
              <p>{f.body}</p>
            </article>
          ))}
        </div>
      </div>
    </section>
  );
}
