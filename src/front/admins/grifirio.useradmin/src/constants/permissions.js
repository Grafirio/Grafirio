/**
 * Izin anahtarlarinin etiketleri: MODUL.AKSIYON.
 *
 * Anahtarlarin kendisi sunucudan geliyor (GET /permissions/actions); burada
 * yalnizca insan okuyacagi karsiliklari duruyor. Liste kopyalanmiyor cunku
 * kopyalanan liste sunucudaki kuraldan sessizce ayrilir.
 *
 * Taninmayan bir anahtar geldiginde ham anahtar gosteriliyor; ekran eksik
 * etiket yuzunden bos kalmasin.
 */

/** Aksiyon parcasinin (nokta sonrasi) etiketi — matrisin sutun basliklari. */
const ACTION_LABELS = {
  READ: 'Görme',
  CREATE: 'Ekleme',
  UPDATE: 'Değiştirme',
  DELETE: 'Silme',
  MANAGE: 'Yönetme',
  ASSIGN: 'Atama',
  MANAGE_PERMISSIONS: 'İzin düzenleme',
  MANAGE_MEMBERSHIP: 'Üyelik yönetimi',
  CREATE_CHILD: 'Alt şirket açma',
};

/**
 * Aksiyonun ne demek oldugu belirsiz kaldigi yerlerde ek aciklama. Yalnizca
 * gerektigi yerde var: her satira aciklama koymak tabloyu okunmaz yapiyor.
 */
const PERMISSION_HINTS = {
  'PANEL.READ': 'Panele giriş. Üyelikle geliyor; izin kümesiyle kaldırılamaz.',
  'DATA_SOURCES.UPDATE': 'Bağlantıyı değiştirmek, tablo seçimini ve analizini kaydetmek.',
  'USERS.MANAGE_MEMBERSHIP': 'Birini admin yapmak ya da adminliğini geri almak.',
  'COMPANY_SETTINGS.CREATE_CHILD': 'Yeni bir tüzel kişilik açmak; şirket bilgisini düzeltmekten ayrı.',
  'ROLES.ASSIGN': 'Kullanıcıya rol vermek, geri almak.',
  'ROLES.MANAGE_PERMISSIONS': 'Rollerin izin kümesini ve kişiye özel izinleri düzenlemek.',
  'BILLING.MANAGE': 'Paket başlatmak, iptal etmek.',
};

export const actionOf = (permission) => permission.split('.')[1] ?? permission;

export const moduleOf = (permission) => permission.split('.')[0];

export const actionLabel = (permission) => {
  const action = actionOf(permission);
  return ACTION_LABELS[action] ?? action;
};

export const permissionHint = (permission) => PERMISSION_HINTS[permission] ?? null;

/**
 * Uyelik seviyeleri. Bir yetki merdiveni degil: kurucu ve admin izin
 * semasinin tamamen disinda, uye tamamen icinde.
 */
export const LEVEL_LABELS = {
  FOUNDER: 'Kurucu',
  ADMIN: 'Admin',
  MEMBER: 'Üye',
  PLATFORM_ADMIN: 'Platform ekibi',
};

export const levelLabel = (code) => LEVEL_LABELS[code] ?? code;

/** Uctan atanabilen seviyeler; kurucu sirket kurulurken belirleniyor. */
export const ASSIGNABLE_LEVELS = ['ADMIN', 'MEMBER'];

export const LEVEL_HINTS = {
  FOUNDER: 'Şirketi kuran kişi. Her şeye erişir, seviyesi değiştirilemez.',
  ADMIN: 'Her şeye erişir; izin kümesiyle sınırlandırılamaz.',
  MEMBER: 'Yetkisi tamamen rollerinden ve kişisel izinlerinden gelir.',
};
