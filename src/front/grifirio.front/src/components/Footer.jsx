import Wordmark from './Wordmark';
import { CONTACT_EMAIL } from '../config';

const links = [
  { label: 'Ürün', href: '#ozellikler' },
  { label: 'Fiyatlandırma', href: '#fiyatlar' },
  { label: 'S.S.S.', href: '#sss' },
  { label: 'İletişim', href: `mailto:${CONTACT_EMAIL}` },
];

export default function Footer() {
  return (
    <footer className="footer">
      <div className="wrap footer-inner">
        <Wordmark size={26} />
        <span className="footer-tagline">Verini keşfet, geleceği gör.</span>

        <nav className="footer-links" aria-label="Alt menü">
          {links.map((l) => (
            <a key={l.label} href={l.href}>
              {l.label}
            </a>
          ))}
        </nav>
      </div>
    </footer>
  );
}
