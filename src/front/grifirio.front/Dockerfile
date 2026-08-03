# Build stage
FROM node:20-alpine AS build

# Derleme baglami src/front. Paylasilan tema (shared/theme/tokens.css) uygulama
# klasorunun disinda duruyor ve CSS ona goreli yolla ulasiyor; baglam yalnizca
# uygulama klasoru oldugunda dosya container icinde hic bulunmuyordu.
#
# Klasor duzeni repodakiyle ayni tutuluyor: grifirio.front/src/styles icinden
# "../../../shared" hem burada hem repoda ayni yere cikiyor. Duzlestirirsek yol
# bir daha kayar.
WORKDIR /app

ARG VITE_APP_URL=https://admin.grafirio.com
ENV VITE_APP_URL=$VITE_APP_URL

ARG VITE_KEYCLOAK_URL=https://login.grafirio.com
ARG VITE_KEYCLOAK_REALM=grafirio
ARG VITE_KEYCLOAK_CLIENT_ID=grafirio-client

ENV VITE_KEYCLOAK_URL=$VITE_KEYCLOAK_URL
ENV VITE_KEYCLOAK_REALM=$VITE_KEYCLOAK_REALM
ENV VITE_KEYCLOAK_CLIENT_ID=$VITE_KEYCLOAK_CLIENT_ID

COPY grifirio.front/package*.json ./grifirio.front/
WORKDIR /app/grifirio.front
RUN npm ci

WORKDIR /app
COPY shared ./shared
COPY grifirio.front ./grifirio.front

WORKDIR /app/grifirio.front
RUN npm run build

# Production stage
FROM node:20-alpine AS production
WORKDIR /app

COPY --from=build /app/grifirio.front/package*.json ./
COPY --from=build /app/grifirio.front/node_modules ./node_modules
# "vite preview" bu dosyayi calisma aninda okur; olmadan varsayilanlara doner
# ve tanimadigi Host basliklarini 403 ile reddeder.
COPY --from=build /app/grifirio.front/vite.config.js ./
COPY --from=build /app/grifirio.front/dist ./dist

EXPOSE 59264

CMD ["npm", "run", "preview", "--", "--host", "0.0.0.0", "--port", "59264"]
