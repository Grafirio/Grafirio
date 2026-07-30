import { useState } from 'react';

const items = [
  {
    q: 'Verimi yüklemek zorunda mıyım?',
    a: 'Hayır. Excel veya CSV yükleyebilirsin, ama istersen veri tabanına doğrudan bağlanırsın — o durumda sorgu kendi sunucunda çalışır, veri yerinde kalır.',
  },
  {
    q: 'SQL bilmem gerekiyor mu?',
    a: 'Gerekmiyor. Soruyu gündelik Türkçeyle yaz; Grafirio doğru grafiği seçip kanvasa ekler. İstersen üretilen sorguyu görebilir, elle düzenleyebilirsin.',
  },
  {
    q: 'Hangi kaynakları destekliyorsunuz?',
    a: 'Dosya tarafında CSV ve XLSX; veri tabanı tarafında PostgreSQL, MySQL, SQL Server ve Oracle. Listede olmayan bir kaynağın varsa Kurumsal pakette özel entegrasyon yapıyoruz.',
  },
  {
    q: 'Verim yapay zekâ eğitiminde kullanılıyor mu?',
    a: 'Hayır. Veriler AB sunucularında tutulur ve model eğitiminde kullanılmaz. Yalnızca senin sorularını cevaplamak için işlenir.',
  },
  {
    q: 'Şirketimizin kendi girişini kullanabilir miyiz?',
    a: 'Evet. Kimlik doğrulama Keycloak üzerinden yürüyor, dolayısıyla mevcut kurumsal kimlik sağlayıcına bağlanıp tek oturum açma kurabilirsin.',
  },
  {
    q: 'Deneme süresi bitince ne oluyor?',
    a: 'Hesabın ve kurduğun kanvaslar duruyor, yalnızca erişim kapanıyor. Bir paket seçtiğinde bıraktığın yerden devam ediyorsun.',
  },
];

export default function Faq() {
  const [open, setOpen] = useState(0);

  return (
    <section className="band" id="sss">
      <div className="wrap">
        <div className="section-head is-center">
          <p className="mono eyebrow" style={{ color: '#4B4A9F' }}>
            Sık sorulanlar
          </p>
          <h2 className="section-title">Merak edilenler</h2>
        </div>

        <div className="faq-list">
          {items.map((it, i) => {
            const isOpen = open === i;
            return (
              <div className={`faq-item ${isOpen ? 'is-open' : ''}`} key={it.q}>
                <button
                  type="button"
                  className="faq-q"
                  onClick={() => setOpen(isOpen ? -1 : i)}
                  aria-expanded={isOpen}
                >
                  <span>{it.q}</span>
                  <span className="faq-sign" aria-hidden="true" />
                </button>
                <div className="faq-a" hidden={!isOpen}>
                  <p>{it.a}</p>
                </div>
              </div>
            );
          })}
        </div>
      </div>
    </section>
  );
}
