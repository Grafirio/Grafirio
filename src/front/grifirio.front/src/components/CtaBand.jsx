import GMark from './GMark';
import { SIGN_UP_URL } from '../config';

export default function CtaBand() {
  return (
    <section className="section cta-section">
      <div className="wrap">
        <div className="cta">
          <span className="cta-mark" aria-hidden="true">
            <GMark barColor="#FBFAF8" />
          </span>

          <div className="cta-copy">
            <h2>İlk dashboard’un 2 dakika sürsün</h2>
            <p>Kredi kartı yok, kurulum yok. Bir dosya yükle ve gör.</p>
          </div>

          <a className="btn btn-sun btn-lg cta-btn" href={SIGN_UP_URL}>
            Ücretsiz başla
            <span className="btn-arrow" aria-hidden="true">
              →
            </span>
          </a>
        </div>
      </div>
    </section>
  );
}
