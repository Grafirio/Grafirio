# Paylaşılan paketlerin sürümü

`shared-packages.ref`, [Grafirio/Packages](https://github.com/Grafirio/Packages)
deposundan hangi commit'in derleneceğini söyler. .NET servislerinin hepsi
`Grafirio.Shared.*` paketlerini bu ref'ten üretir.

## Neden dal değil de commit

Önceden bütün Dockerfile'lar `main` dalını çekiyordu ve bunun iki somut sonucu
vardı:

1. **Servisler birbirinden ayrışıyordu.** CI yalnızca dosyası değişen servisi
   yeniden derliyor. Packages ilerlediğinde, o gün derlenen servis yeni
   kütüphaneyle, ötekiler eskisiyle çalışmaya devam ediyordu. Aynı sistemde iki
   farklı sözleşme demek.

2. **Bu depoda hiçbir şey değişmeden CI kırılabiliyordu.** Paylaşılan bir tip
   zorunlu bir alan kazandığında, buradaki çağrı noktaları uyumsuz kalıyor ve
   derleme, kimsenin dokunmadığı bir anda düşüyordu.

İkisi de 2026-08-20'de aynı gün yaşandı: `EffectivePermissions` yeni bir zorunlu
alan kazandığı için `identity-api` derlenemez oldu; ayrıca `PermissionAuthority`
düzeltmesi `data-analysis-api`'ye ulaşmadığı için yetki arızası, düzeltme merge
edildikten sonra da sürdü.

Commit'e sabitlemek ikisini birden kapatıyor: hangi sürümle derlendiği açık, ve
sürümü değiştirmek bu depoda görünür bir commit oluyor.

## Nasıl güncellenir

```bash
git ls-remote https://github.com/Grafirio/Packages.git refs/heads/main
```

Çıkan SHA'yı `shared-packages.ref` içine yazıp commit'leyin. Bu dosya
değiştiğinde CI, paketi kullanan **bütün** .NET servislerini birlikte yeniden
derler — yarısı yeni yarısı eski kalmaz.

Yerelde farklı bir ref denemek için:

```bash
docker build --build-arg SHARED_PACKAGES_REF=main -f <dockerfile> .
```
