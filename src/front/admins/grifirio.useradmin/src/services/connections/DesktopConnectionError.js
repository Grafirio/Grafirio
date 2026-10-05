const CONNECTION_FAILURE = 'Bu bilgisayardan bağlantı kurulamadı.';

// Masaüstü kabuğunun bildirdiği sebep, mesajın sonuna ekleniyor. Öncesinde her arıza aynı
// "Masaüstü bağlantısı hazır değil" yazısıyla çıkıyordu: oturum düşmesi de, buluta
// bağlanamama da, zaman aşımı da. Gerçek sebebi öğrenmenin tek yolu kullanıcının
// makinesindeki günlük dosyasını açmaktı.
const describe = (code) => code === 'signedOut'
  ? 'Lütfen yeniden giriş yapın ve tekrar deneyin.'
  : code === 'differentBridge'
    ? 'Kayıtlı bağlantı bu bilgisayara bağlı değil. Bu bilgisayarda kullanmak için bağlantıyı düzenleyip Kaydet düğmesine basın.'
    : 'Masaüstü bağlantısı hazır değil. Lütfen aynı düğmeyle tekrar deneyin.';

export default class DesktopConnectionError extends Error {
  constructor(code = 'unavailable', reason = null) {
    const detail = typeof reason === 'string' && reason.trim() ? ` (${reason.trim()})` : '';
    super(`${CONNECTION_FAILURE} ${describe(code)}${detail}`);
    this.name = 'DesktopConnectionError';
    this.code = code;
    this.reason = detail ? reason.trim() : null;
  }
}