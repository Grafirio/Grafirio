const CONNECTION_FAILURE = 'Bu bilgisayardan bağlantı kurulamadı.';

// Masaüstü kabuğunun bildirdiği sebep, mesajın sonuna ekleniyor. Öncesinde her arıza aynı
// "Masaüstü bağlantısı hazır değil" yazısıyla çıkıyordu: oturum düşmesi de, buluta
// bağlanamama da, zaman aşımı da. Gerçek sebebi öğrenmenin tek yolu kullanıcının
// makinesindeki günlük dosyasını açmaktı.
const describe = (code) => code === 'signedOut'
  ? 'Lütfen yeniden giriş yapın ve tekrar deneyin.'
  : code === 'differentBridge'
    ? 'Kayıtlı bağlantı bu bilgisayara bağlı değil. Bu bilgisayarda kullanmak için bağlantıyı düzenleyip Kaydet düğmesine basın.'
    : 'Masaüstü bağlantısı hazır değil. Bulut ve cihaz bağlantı durumunu kontrol edin.';

export default class DesktopConnectionError extends Error {
  constructor(code = 'unavailable', reason = null, retryable = true) {
    const detail = typeof reason === 'string' && reason.trim() ? ` (${reason.trim()})` : '';
    super(`${CONNECTION_FAILURE} ${detail ? reason.trim() : describe(code)}`);
    this.name = 'DesktopConnectionError';
    this.code = code;
    this.reason = detail ? reason.trim() : null;
    this.retryable = retryable;
  }
}
