import axios from 'axios';

const GATEWAY = import.meta.env.VITE_API_URL || '';

// Odeme saglayicisi henuz secilmedi. Gercek kart bilgisi toplamayan, acikca
// isaretli bir gecis kullaniliyor; boylece akis uctan uca denenebiliyor ama
// kart verisi hicbir yerde dolasmiyor. Saglayici baglaninca burasi onun
// formuyla degisir ve kart bilgisi bizim sunucumuza hic ugramaz.
export const TEST_PAYMENT_ENABLED =
  String(import.meta.env.VITE_ALLOW_TEST_PAYMENT ?? 'true') === 'true';

const TEST_CARD = {
  CardNumber: '4242424242424242',
  CardHolderName: 'TEST KART',
  CardExpirationDate: '12/30',
  CardSecurityNumber: '123',
};

const auth = (token) => ({ headers: { Authorization: `Bearer ${token}` }, timeout: 20000 });

/** Katalogdaki abonelik planlari. Sirali urun listesinden plan olanlar suzuluyor. */
export const fetchPlans = async (token) => {
  const { data } = await axios.get(`${GATEWAY}/v1/catalogs/products`, auth(token));
  const products = data?.data ?? data ?? [];
  return products.filter((p) => p.subscriptionPlan || p.SubscriptionPlan);
};

/**
 * Firma kaydi. Abonelik firmaya baglandigi icin bu adim odemeden once
 * tamamlanmali: OrderPaidConsumer, alicinin bir firmaya bagli olmadigini
 * gorurse abonelik acmiyor ve "para alindi, erisim verilmedi" durumu olusuyor.
 */
export const createCompany = async (token, { name, code, description }) => {
  const { data } = await axios.post(
    `${GATEWAY}/v1/identity/companies`,
    { name, code: code || null, description: description || null, parentCompanyId: null },
    auth(token)
  );
  return data?.data ?? data;
};

export const createOrder = async (token, { productId, quantity = 1, address }) => {
  const { data } = await axios.post(
    `${GATEWAY}/v1/orders`,
    {
      discountRate: null,
      address,
      items: [{ productId, quantity }],
    },
    auth(token)
  );
  return data?.data ?? data;
};

export const payOrder = async (token, { orderCode, amount }) => {
  if (!TEST_PAYMENT_ENABLED) {
    throw new Error('Ödeme sağlayıcısı bağlı değil.');
  }
  const { data } = await axios.post(
    `${GATEWAY}/v1/payments`,
    { orderCode, amount, ...TEST_CARD },
    auth(token)
  );
  return data?.data ?? data;
};
