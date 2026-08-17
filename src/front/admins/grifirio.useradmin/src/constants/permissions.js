/**
 * Izin anahtarlarinin etiketleri: MODUL.AKSIYON.
 *
 * Anahtarlarin kendisi sunucudan geliyor (GET /permissions/actions); burada
 * yalnizca insan okuyacagi karsiliklari duruyor. Liste kopyalanmiyor cunku
 * kopyalanan liste sunucudaki kuraldan sessizce ayrilir — daha once tam olarak
 * bu oldu: Yetki Ayarlari ekranindaki matris elle yazilmisti.
 *
 * Taninmayan bir anahtar geldiginde ham anahtar gosteriliyor; ekran eksik
 * etiket yuzunden bos kalmasin.
 */

/** Aksiyon parcasinin (nokta sonrasi) etiketi. */
const ACTION_LABELS = {
  READ: 'Görme',
  CREATE: 'Ekleme',
  UPDATE: 'Değiştirme',
  DELETE: 'Silme',
  MANAGE: 'Yönetme',
  MANAGE_ROLES: 'Rol atama',
  ASSIGN_MEMBERS: 'Üye atama',
  MANAGE_PERMISSIONS: 'İzin düzenleme',
  CREATE_CHILD: 'Alt şirket açma',
};

/**
 * Aksiyonun ne demek oldugu belirsiz kaldigi yerlerde ek aciklama. Yalnizca
 * gerektigi yerde var: her satira aciklama koymak tabloyu okunmaz yapiyor.
 */
const PERMISSION_HINTS = {
  'PANEL.READ': 'Panele giriş. Departman bu izni kaldıramaz; role bağlı.',
  'DATA_SOURCES.UPDATE': 'Bağlantıyı değiştirmek ve tablo seçimini kaydetmek.',
  'USERS_ROLES.MANAGE_ROLES': 'Kimin yönetici, müdür ya da kullanıcı olacağına karar vermek.',
  'COMPANY_SETTINGS.CREATE_CHILD': 'Yeni bir tüzel kişilik açmak; şirket bilgisini düzeltmekten ayrı.',
  'DEPARTMENTS.MANAGE_PERMISSIONS': 'Departmanın izin kümesini düzenlemek.',
  'BILLING.MANAGE': 'Paket başlatmak, iptal etmek.',
};

export const actionOf = (permission) => permission.split('.')[1] ?? permission;

export const actionLabel = (permission) => {
  const action = actionOf(permission);
  return ACTION_LABELS[action] ?? action;
};

export const permissionHint = (permission) => PERMISSION_HINTS[permission] ?? null;

/** Rol kodlarinin etiketi; platform rolu COMPANY_ROLES listesinde yok. */
export const ROLE_LABELS = {
  PLATFORM_ADMIN: 'Platform ekibi',
  COMPANY_ADMIN: 'Yönetici',
  COMPANY_MANAGER: 'Müdür',
  COMPANY_USER: 'Kullanıcı',
};

export const roleLabel = (code) => ROLE_LABELS[code] ?? code;
