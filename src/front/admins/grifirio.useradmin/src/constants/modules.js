/**
 * Panelin modulleri. Anahtarlar sunucudaki AppModules ile birebir ayni olmak
 * zorunda; ayrisirlarsa menu bir sey gosterir, sunucu baskasini uygular.
 *
 * Buradaki liste yalnizca etiketleme icin. Kullanicinin hangi modullere
 * girebildigi sunucudan geliyor (GET /permissions/me/{companyId}).
 */
export const MODULES = [
  { key: 'ANALYSIS', name: 'Analizler', description: 'Kanvas, dashboard ve soru sorma.' },
  { key: 'DATA_SOURCES', name: 'Veri kaynakları', description: 'Veritabanı bağlama ve tablo seçimi.' },
  { key: 'DOCUMENTS', name: 'Belgeler', description: 'Kurumsal evrak yükleme ve görüntüleme.' },
  { key: 'COMPANY_SETTINGS', name: 'Şirket ayarları', description: 'Yasal kimlik, adres, banka.' },
  { key: 'USERS_ROLES', name: 'Kullanıcılar ve yetkiler', description: 'Kim var, hangi rolde.' },
  { key: 'DEPARTMENTS', name: 'Departmanlar', description: 'Organizasyon birimleri ve izinleri.' },
  { key: 'BILLING', name: 'Üyelik', description: 'Paket ve fatura.' },
];

export const moduleName = (key) => MODULES.find((m) => m.key === key)?.name ?? key;

export const MODULE = {
  ANALYSIS: 'ANALYSIS',
  DATA_SOURCES: 'DATA_SOURCES',
  DOCUMENTS: 'DOCUMENTS',
  COMPANY_SETTINGS: 'COMPANY_SETTINGS',
  USERS_ROLES: 'USERS_ROLES',
  DEPARTMENTS: 'DEPARTMENTS',
  BILLING: 'BILLING',
};
