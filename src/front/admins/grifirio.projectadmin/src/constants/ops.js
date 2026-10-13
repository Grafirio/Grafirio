export const STATUS_POLL_INTERVAL_MS = 5000;
export const OPS_REQUEST_TIMEOUT_MS = 10000;
export const AUDIT_LOG_LIMIT = 50;
export const RECOVERY_URL = 'http://127.0.0.1:3001/recovery/';

export const SERVICE_STATUS = { UP: 'up', DOWN: 'down', UNKNOWN: 'unknown' };
export const SERVICE_ACTION = { START: 'start', STOP: 'stop', RESTART: 'restart' };

// Stopping or restarting interrupts users; starting never does.
export const CONFIRMED_ACTIONS = new Set([SERVICE_ACTION.STOP, SERVICE_ACTION.RESTART]);

export const ACTION_LABELS = {
  [SERVICE_ACTION.START]: 'Başlat',
  [SERVICE_ACTION.STOP]: 'Durdur',
  [SERVICE_ACTION.RESTART]: 'Yeniden başlat',
};

export const STATUS_BADGES = {
  [SERVICE_STATUS.UP]: { label: 'Çalışıyor', className: 'gf-badge gf-badge--success' },
  [SERVICE_STATUS.DOWN]: { label: 'Kapalı', className: 'gf-badge gf-badge--danger' },
  [SERVICE_STATUS.UNKNOWN]: { label: 'Bilinmiyor', className: 'gf-badge' },
};

export const OPERATION_LABELS = {
  'docker-start': 'Docker başlatma',
  'infrastructure-up': 'Altyapıyı kaldırma',
};

export const EVENT_LABELS = {
  down: 'Düştü',
  recovered: 'Düzeldi',
  'autoheal-exhausted': 'Otomatik onarma vazgeçti',
};

export const CATEGORY_ORDER = ['Database', 'Messaging', 'Identity', 'Application', 'AI'];
export const CATEGORY_LABELS = {
  Database: 'Veritabanları',
  Messaging: 'Mesajlaşma',
  Identity: 'Kimlik',
  Application: 'Uygulamalar',
  AI: 'Yapay zekâ',
};

export const AUTO_HEAL_FIELDS = [
  { key: 'failureThreshold', label: 'Başarısız yoklama eşiği', min: 1, max: 10,
    hint: 'Bu kadar art arda başarısız yoklamadan sonra müdahale edilir.' },
  { key: 'maxAttempts', label: 'En fazla deneme', min: 1, max: 10,
    hint: 'Aşılırsa ajan vazgeçer ve uyarı verir.' },
  { key: 'cooldownSeconds', label: 'Denemeler arası bekleme (sn)', min: 10, max: 3600,
    hint: 'Her denemeden sonra sonucu görmek için beklenen süre.' },
];
