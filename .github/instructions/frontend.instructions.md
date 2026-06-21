---
applyTo: "**/*.{js,jsx,ts,tsx}"
description: "Frontend (React / Vite) kod kuralları — component bölme, klasörleme, hook'lar, secret yönetimi"
---

# Frontend Kod Kuralları (React / Vite)

Bu kurallar tüm `.js`, `.jsx`, `.ts`, `.tsx` dosyalarına uygulanır. `.github/copilot-instructions.md` içindeki genel kurallar geçerliliğini korur — özellikle **ONAYLANDI kuralı**.

---

## 1. Klasör ve Dosya Yapısı

- **Bir klasör çok dosya biriktirmesin.** Klasör şişiyorsa alt klasörlere böl.
- **1 dosya = 1 component / hook / util fonksiyonu.**
- Component büyürse alt component'lere ayrılır ve ayrı dosyalara taşınır.
- Sayısal satır limiti yok; "mantıklı şekilde böl" prensibi geçerlidir.

Önerilen klasör yapısı:

```
src/
├── components/
│   ├── common/              # Button, Input, Modal gibi paylaşılan UI
│   ├── layout/              # Header, Sidebar, Footer
│   └── orders/              # Feature bazlı
│       ├── OrderList.jsx
│       ├── OrderItem.jsx
│       └── OrderFilter.jsx
├── pages/                   # Route-level component'ler
├── hooks/                   # Custom hook'lar (useXxx)
├── services/                # API çağrıları, axios instance
├── store/                   # State management (Redux / Zustand / Context)
├── utils/                   # Pure helper fonksiyonlar
├── constants/               # Sabit değerler, enum'lar
├── types/                   # TypeScript tip tanımları
└── assets/                  # Resim, font, icon
```

---

## 2. Component Kuralları

- **Tek component, tek dosya, tek sorumluluk.**
- Component içinde 200 satır üstüne çıkıyorsa veya birden çok mantıksal blok varsa → alt component'lere ayır.
- JSX içinde karmaşık koşullu render → ayrı küçük component.
- Inline fonksiyonu render içinde tanımlama; yukarıda named function veya `useCallback` kullan.

**Doğru:**
```jsx
// src/components/orders/OrderList.jsx
function OrderList({ orders }) {
  return (
    <ul>
      {orders.map((order) => (
        <OrderItem key={order.id} order={order} />
      ))}
    </ul>
  );
}
```

**Yanlış:** Tek dosyada `OrderList`, `OrderItem`, `OrderFilter`, `OrderForm` birlikte.

---

## 3. Mantığı Component'ten Ayırma

- **API çağrıları** → `services/` altındaki dosyalardan. Component içinde direkt `fetch` / `axios.get` yok.
- **Reusable state mantığı** → custom hook (`hooks/useOrders.js`).
- **Saf hesaplama** → `utils/` altında pure function.

```jsx
// hooks/useOrders.js
export function useOrders() {
  const [orders, setOrders] = useState([]);
  useEffect(() => {
    orderService.getAll().then(setOrders);
  }, []);
  return orders;
}
```

---

## 4. Naming

| Öğe | Kural | Örnek |
|---|---|---|
| Component | `PascalCase`, dosya adı component ile aynı | `OrderList.jsx` → `OrderList` |
| Hook | `useXxx` | `useOrders` |
| Util fonksiyon | `camelCase` | `formatCurrency` |
| Constant | `SCREAMING_SNAKE_CASE` | `MAX_RETRY_COUNT` |
| Boolean değişken | `is`, `has`, `should` prefix | `isLoading`, `hasError` |
| Event handler | `handle` prefix | `handleSubmit` |

---

## 5. Magic Değer Yok

Tüm sabitler `constants/` altında veya dosya başında tanımlanır:

```js
// constants/api.js
export const API_TIMEOUT_MS = 30000;
export const MAX_PAGE_SIZE = 50;
```

---

## 6. Stil

- Tailwind / CSS Modules / Styled Components — proje hangisini kullanıyorsa ona uy.
- Inline `style` mümkün olduğunca az; dinamik değer dışında kullanma.
- Renk, spacing, font-size sabitleri tema / config dosyasında.

---

## 7. Hassas Veri (Frontend'e Özel)

- `.env.local` → geliştirme; **git'e gitmez** (`.gitignore` içinde).
- `.env.example` → repo'da bulunan şablon.
- Vite kullanıyorsanız değişkenler `VITE_` prefix'i ile başlamalıdır (örn. `VITE_API_BASE_URL`).
- **Tarayıcıya gönderilen JS bundle'a secret konmaz.** API key gibi gerçek secret'lar backend'den proxy'lenir.
- Token'lar `localStorage` yerine `httpOnly` cookie tercih edilir (mümkünse).

---

## 8. State Management

- Component-local state → `useState`.
- Birkaç component arası → context veya prop drilling (3 seviyeye kadar).
- Global state → tek bir store (Redux / Zustand). Karma kullanım yok.
- Server state → React Query / SWR tercih edilir.

---

## 9. Error Handling

- API çağrılarında hata mutlaka yakalanır ve kullanıcıya gösterilir (toast, hata mesajı).
- `console.log` ile bırakılmaz; gerekirse logger servisi (Sentry, vb.) kullan.
- Hata susturma yok.

---

## 10. Performance

- Liste render'larında `key` zorunlu, `index` yerine stabil id.
- Ağır hesaplama → `useMemo`.
- Sık değişen callback prop → `useCallback`.
- Route bazlı kod bölme → `React.lazy` + `Suspense`.

---

## 11. Linting

- ESLint kuralları repo'daki `eslint.config.js` üzerinden uygulanır.
- Lint hatası olan kod commit'lenmez.
- Unused import / variable temizlenir.
