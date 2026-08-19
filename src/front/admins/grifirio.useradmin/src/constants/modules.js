/**
 * Panelin modulleri ve izin anahtarlari. Anahtarlar sunucudaki AppModules /
 * AppPermissions ile birebir ayni olmak zorunda; ayrisirlarsa menu bir sey
 * gosterir, sunucu baskasini uygular.
 *
 * Buradaki listeler yalnizca etiketleme ve cagri yerlerinde okunakli sabit
 * icin. Kullanicinin neye izinli oldugu sunucudan geliyor
 * (GET /permissions/me/{companyId}); departman matrisinin cizdigi
 * modul -> aksiyon agaci da sunucudan (GET /permissions/actions).
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

/**
 * Izin anahtarlari (MODUL.AKSIYON) — sunucudaki AppPermissions'in aynasi.
 *
 * Cagri yerlerinde ham metin yazmamak icin: can('DATA_SOURCES.UPDATE') bir
 * harf hatasinda sessizce "yetkisi yok" derdi, PERM.DATA_SOURCES_UPDATE ise
 * derlenmeden once goze batar.
 */
export const PERM = {
  PANEL_READ: 'PANEL.READ',

  ANALYSIS_READ: 'ANALYSIS.READ',
  ANALYSIS_CREATE: 'ANALYSIS.CREATE',
  ANALYSIS_DELETE: 'ANALYSIS.DELETE',

  DATA_SOURCES_READ: 'DATA_SOURCES.READ',
  DATA_SOURCES_CREATE: 'DATA_SOURCES.CREATE',
  DATA_SOURCES_UPDATE: 'DATA_SOURCES.UPDATE',
  DATA_SOURCES_DELETE: 'DATA_SOURCES.DELETE',

  DOCUMENTS_READ: 'DOCUMENTS.READ',
  DOCUMENTS_CREATE: 'DOCUMENTS.CREATE',
  DOCUMENTS_DELETE: 'DOCUMENTS.DELETE',

  COMPANY_SETTINGS_READ: 'COMPANY_SETTINGS.READ',
  COMPANY_SETTINGS_UPDATE: 'COMPANY_SETTINGS.UPDATE',
  COMPANY_SETTINGS_CREATE_CHILD: 'COMPANY_SETTINGS.CREATE_CHILD',

  USERS_READ: 'USERS_ROLES.READ',
  USERS_CREATE: 'USERS_ROLES.CREATE',
  USERS_MANAGE_ROLES: 'USERS_ROLES.MANAGE_ROLES',

  DEPARTMENTS_READ: 'DEPARTMENTS.READ',
  DEPARTMENTS_CREATE: 'DEPARTMENTS.CREATE',
  DEPARTMENTS_UPDATE: 'DEPARTMENTS.UPDATE',
  DEPARTMENTS_DELETE: 'DEPARTMENTS.DELETE',
  DEPARTMENTS_ASSIGN_MEMBERS: 'DEPARTMENTS.ASSIGN_MEMBERS',
  DEPARTMENTS_MANAGE_PERMISSIONS: 'DEPARTMENTS.MANAGE_PERMISSIONS',

  BILLING_READ: 'BILLING.READ',
  BILLING_MANAGE: 'BILLING.MANAGE',
};

/** 'DATA_SOURCES.UPDATE' -> 'DATA_SOURCES' */
export const moduleOf = (permission) => String(permission).split('.')[0];

/** 'DATA_SOURCES.UPDATE' -> 'UPDATE' */
export const actionOf = (permission) => String(permission).split('.')[1] ?? '';

// Aksiyonun genel adi. Modul basina anlami degisenler asagida eziliyor:
// belgede CREATE "yukle", veri kaynaginda "bagla" demek ve matriste
// yedi kez "Ekle" yazmak kullaniciyi neyi actigi konusunda yanlis bilgilendirir.
const ACTION_LABELS = {
  READ: 'Görüntüle',
  CREATE: 'Ekle',
  UPDATE: 'Düzenle',
  DELETE: 'Sil',
  MANAGE: 'Yönet',
};

const PERMISSION_LABELS = {
  [PERM.ANALYSIS_CREATE]: 'Analiz oluştur',
  [PERM.ANALYSIS_DELETE]: 'Analiz sil',
  [PERM.DATA_SOURCES_CREATE]: 'Sunucu bağla',
  [PERM.DATA_SOURCES_UPDATE]: 'Bağlantıyı düzenle',
  [PERM.DATA_SOURCES_DELETE]: 'Bağlantıyı sil',
  [PERM.DOCUMENTS_CREATE]: 'Belge yükle',
  [PERM.DOCUMENTS_DELETE]: 'Belge sil',
  [PERM.COMPANY_SETTINGS_CREATE_CHILD]: 'Alt şirket aç',
  [PERM.USERS_CREATE]: 'Kullanıcı ekle',
  [PERM.USERS_MANAGE_ROLES]: 'Rol ata',
  [PERM.DEPARTMENTS_ASSIGN_MEMBERS]: 'Üye ata',
  [PERM.DEPARTMENTS_MANAGE_PERMISSIONS]: 'İzinleri düzenle',
  [PERM.BILLING_MANAGE]: 'Üyeliği yönet',
};

/** Matriste ve rozetlerde gorunen ad; bilinmeyen anahtar ham haliyle doner. */
export const permissionName = (permission) =>
  PERMISSION_LABELS[permission] ?? ACTION_LABELS[actionOf(permission)] ?? permission;
