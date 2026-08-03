# Keycloak'in kendisi stok imaj; tek eklediğimiz, giris ekranlarinin temasi.
#
# Tema docker-compose'da bir volume olarak baglaniyordu, bu yuzden yalnizca
# yerelde vardi: Azure'daki container stok imaji calistirdigi icin musteri
# Keycloak'in varsayilan ekranini goruyordu. Imaja kopyalanunca her ortamda ayni
# ekran cikiyor.
FROM quay.io/keycloak/keycloak:25.0

COPY --chown=keycloak:root docker-keycloak-themes/grifirio /opt/keycloak/themes/grifirio

# Marka degerlerinin tek kaynagi. Tema CSS'i Vite'tan gecmedigi icin degiskenleri
# derleme aninda iceri gomemiyoruz; dosya temanin yanina kopyalaniyor ve
# grafirio.css onu "@import 'tokens.css'" ile aliyor. Kopyalanmazsa giris ekrani
# renksiz acilir.
COPY --chown=keycloak:root src/front/shared/theme/tokens.css \
     /opt/keycloak/themes/grifirio/login/resources/css/tokens.css

# Realm, bos bir veritabaninin kendini toparlamasi icin her aciliista
# yeniden iceri aliniyor (--import-realm).
COPY --chown=keycloak:root keycloak/realm-grafirio.json /opt/keycloak/data/import/realm-grafirio.json
