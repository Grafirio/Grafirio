import GMark from './GMark';

// Yatay kilit. Marka yuksekligi kelime markasinin cap yuksekliginin yaklasik
// iki kati; kilavuzdaki oran bu.
export default function Wordmark({ light = false, tagline = false, size = 34, href = '#top' }) {
  const Tag = href ? 'a' : 'div';

  return (
    <Tag className={`wordmark ${light ? 'is-light' : ''}`} href={href} aria-label="Grafirio">
      <span className="wordmark-mark" style={{ width: size, height: size }}>
        <GMark compact={size < 64} barColor={light ? '#FBFAF8' : '#1C3F7C'} />
      </span>
      <span className="wordmark-text">
        <span className="wordmark-name">GRAFIRIO</span>
        {tagline && <span className="wordmark-tagline">Verini keşfet, geleceği gör.</span>}
      </span>
    </Tag>
  );
}
