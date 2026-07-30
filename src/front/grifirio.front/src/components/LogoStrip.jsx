// DIKKAT: Bu isimler tasarim taslagindan geldi ve gercek musteri degil.
// "Kullananlar" basligi altinda uydurma sirket adi yayinlamak ziyaretciye
// yanlis beyan olur. Yayina cikmadan once ya gercek musterilerle degistirin ya
// da bolumu kaldirin — App.jsx'ten <LogoStrip /> satirini silmek yeterli.
const customers = [
  'Anadolu Lojistik',
  'Vera Retail',
  'Tarım Kredi',
  'Nova Enerji',
  'Kıyı Turizm',
];

export default function LogoStrip() {
  return (
    <div className="logo-strip">
      <div className="wrap logo-strip-inner">
        <span className="mono logo-strip-label">Kullananlar</span>
        <div className="logo-strip-names">
          {customers.map((c) => (
            <span key={c}>{c}</span>
          ))}
        </div>
      </div>
    </div>
  );
}
