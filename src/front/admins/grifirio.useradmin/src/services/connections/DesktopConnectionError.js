const CONNECTION_FAILURE = 'Bu bilgisayardan bağlantı kurulamadı.';

export default class DesktopConnectionError extends Error {
  constructor(code = 'unavailable') {
    const reason = code === 'signedOut'
      ? 'Lütfen yeniden giriş yapın ve tekrar deneyin.'
      : code === 'differentBridge'
        ? 'Kayıtlı bağlantı bu bilgisayara bağlı değil. Bu bilgisayarda kullanmak için bağlantıyı düzenleyip Kaydet düğmesine basın.'
        : 'Masaüstü bağlantısı hazır değil. Lütfen aynı düğmeyle tekrar deneyin.';
    super(`${CONNECTION_FAILURE} ${reason}`);
    this.name = 'DesktopConnectionError';
    this.code = code;
  }
}