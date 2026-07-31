# Keycloak'in kendisi stok imaj; tek eklediğimiz, giris ekranlarinin temasi.
#
# Tema docker-compose'da bir volume olarak baglaniyordu, bu yuzden yalnizca
# yerelde vardi: Azure'daki container stok imaji calistirdigi icin musteri
# Keycloak'in varsayilan ekranini goruyordu. Imaja kopyalanunca her ortamda ayni
# ekran cikiyor.
FROM quay.io/keycloak/keycloak:25.0

COPY --chown=keycloak:root docker-keycloak-themes/grifirio /opt/keycloak/themes/grifirio

# Realm, bos bir veritabaninin kendini toparlamasi icin her aciliista
# yeniden iceri aliniyor (--import-realm).
COPY --chown=keycloak:root keycloak/realm-grafirio.json /opt/keycloak/data/import/realm-grafirio.json
