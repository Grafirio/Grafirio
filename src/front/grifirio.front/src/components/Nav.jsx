import { useEffect, useState } from 'react';
import Wordmark from './Wordmark';
import { SIGN_IN_URL, SIGN_UP_URL } from '../config';

const links = [
  { href: '#nasil-calisir', label: 'Nasıl çalışır' },
  { href: '#analiz', label: 'Analiz' },
  { href: '#ozellikler', label: 'Özellikler' },
  { href: '#fiyatlar', label: 'Fiyatlandırma' },
  { href: '#sss', label: 'S.S.S.' },
];

export default function Nav() {
  const [open, setOpen] = useState(false);

  // Menu acikken arka planin kaymasi, kucuk ekranda menuyu kullanilmaz yapiyor.
  useEffect(() => {
    document.body.style.overflow = open ? 'hidden' : '';
    return () => {
      document.body.style.overflow = '';
    };
  }, [open]);

  return (
    <header className="nav" id="top">
      <div className="wrap nav-inner">
        <Wordmark size={34} />

        <nav className={`nav-links ${open ? 'is-open' : ''}`} aria-label="Ana menü">
          {links.map((l) => (
            <a key={l.href} href={l.href} onClick={() => setOpen(false)}>
              {l.label}
            </a>
          ))}
          <div className="nav-links-cta">
            <a className="btn btn-quiet" href={SIGN_IN_URL}>
              Giriş yap
            </a>
            <a className="btn btn-primary" href={SIGN_UP_URL}>
              Ücretsiz başla
            </a>
          </div>
        </nav>

        <div className="nav-actions">
          <a className="btn btn-quiet" href={SIGN_IN_URL}>
            Giriş yap
          </a>
          <a className="btn btn-primary" href={SIGN_UP_URL}>
            Ücretsiz başla
          </a>
        </div>

        <button
          className="nav-burger"
          onClick={() => setOpen((v) => !v)}
          aria-expanded={open}
          aria-label={open ? 'Menüyü kapat' : 'Menüyü aç'}
        >
          <span />
          <span />
          <span />
        </button>
      </div>
    </header>
  );
}
