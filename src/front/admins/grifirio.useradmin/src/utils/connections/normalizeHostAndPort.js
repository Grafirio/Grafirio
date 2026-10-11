export default function normalizeHostAndPort(host, port) {
  let address = String(host ?? '').trim();
  let embeddedPort;
  const bracketed = address.match(/^\[([0-9a-f:.]+)\](?:[:,](\d+))?$/i);
  const combined = address.match(/^([^:,]+)[:,](\d+)$/);
  if (bracketed || combined) {
    const match = bracketed ?? combined;
    address = match[1].trim();
    if (match[2]) embeddedPort = Number(match[2]);
  }
  if (bracketed || address.includes(':')) {
    try {
      const parsed = new URL(`http://[${address}]/`);
      if (!parsed.hostname.startsWith('[')) throw new Error();
    } catch {
      throw new Error('Geçerli bir IPv6 adresi girin.');
    }
  }
  if (!address || /[\s/\\;,@?#[\]]/.test(address)
    || (address.includes(':') && !/^[0-9a-f:.]+$/i.test(address))) {
    throw new Error('Geçerli bir sunucu adı veya IP girin; URL veya instance adı yerine TCP portunu kullanın.');
  }
  const explicitPort = port === '' || port == null ? null : Number(port);
  const chosenPort = explicitPort ?? embeddedPort ?? 1433;
  if (!Number.isInteger(chosenPort) || chosenPort < 1 || chosenPort > 65535
    || (embeddedPort != null && (embeddedPort < 1 || embeddedPort > 65535))) {
    throw new Error('Port 1–65535 arasında bir tam sayı olmalıdır.');
  }
  if (embeddedPort != null && explicitPort != null && embeddedPort !== explicitPort) {
    throw new Error('Sunucu adresindeki port ile Port alanı farklı. Aynı portu kullanın veya Port alanını boşaltın.');
  }
  return { host: address, port: chosenPort };
}
