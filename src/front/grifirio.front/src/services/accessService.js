import axios from 'axios';

/**
 * Ürünü kullanma hakkı Identity.Api'de tutulan aboneliğe bağlı.
 * Kural tek yerde tanımlı kalsın diye burada yeniden yorumlanmıyor; sunucunun
 * verdiği cevap olduğu gibi taşınıyor.
 */
const GATEWAY = import.meta.env.VITE_API_URL || '';

export const fetchMyAccess = async (token) => {
  const response = await axios.get(`${GATEWAY}/v1/identity/subscriptions/my-access`, {
    headers: { Authorization: `Bearer ${token}` },
    timeout: 20000,
  });
  return response.data;
};

/** Sunucunun sebep kodunu kullanıcıya gösterilebilir bir cümleye çevirir. */
export const describeAccessReason = (reason) => {
  switch (reason) {
    case 'No subscription':
      return 'Firmanız için henüz bir abonelik tanımlanmamış.';
    case 'Subscription expired':
      return 'Firmanızın aboneliğinin süresi dolmuş.';
    case 'User is not assigned to a company':
      return 'Hesabınız henüz bir firmaya bağlı değil.';
    default:
      return reason || 'Erişim doğrulanamadı.';
  }
};
