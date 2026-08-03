# Build stage
FROM node:20-alpine AS build

# Derleme baglami src/front. Paylasilan tema (shared/theme/tokens.css) uygulama
# klasorunun disinda duruyor ve CSS ona goreli yolla ulasiyor; baglam yalnizca
# uygulama klasoru oldugunda dosya container icinde hic bulunmuyordu.
#
# Klasor duzeni repodakiyle ayni tutuluyor: admins/grifirio.useradmin/src/styles
# icinden "../../../../shared" hem burada hem repoda ayni yere cikiyor.
WORKDIR /app

ARG VITE_KEYCLOAK_URL=https://login.grafirio.com
ARG VITE_KEYCLOAK_REALM=grafirio
ARG VITE_KEYCLOAK_CLIENT_ID=grafirio-client
ARG VITE_API_GATEWAY_URL=https://gateway.proudcoast-6064711b.westeurope.azurecontainerapps.io
# The code reads VITE_API_URL, not VITE_API_GATEWAY_URL. Without it the bundle
# fell back to http://localhost:5000 and every data-analysis call left the
# browser aimed at the user's own machine.
ARG VITE_API_URL=https://gateway.proudcoast-6064711b.westeurope.azurecontainerapps.io

ENV VITE_KEYCLOAK_URL=$VITE_KEYCLOAK_URL
ENV VITE_KEYCLOAK_REALM=$VITE_KEYCLOAK_REALM
ENV VITE_KEYCLOAK_CLIENT_ID=$VITE_KEYCLOAK_CLIENT_ID
ENV VITE_API_GATEWAY_URL=$VITE_API_GATEWAY_URL
ENV VITE_API_URL=$VITE_API_URL

COPY admins/grifirio.useradmin/package*.json ./admins/grifirio.useradmin/
WORKDIR /app/admins/grifirio.useradmin
RUN npm ci

WORKDIR /app
COPY shared ./shared
COPY admins/grifirio.useradmin ./admins/grifirio.useradmin

WORKDIR /app/admins/grifirio.useradmin
RUN npm run build

# Production stage
FROM node:20-alpine AS production
WORKDIR /app

COPY --from=build /app/admins/grifirio.useradmin/package*.json ./
COPY --from=build /app/admins/grifirio.useradmin/node_modules ./node_modules
# "vite preview" bu dosyayi calisma aninda okur; olmadan varsayilanlara doner
# ve tanimadigi Host basliklarini 403 ile reddeder.
COPY --from=build /app/admins/grifirio.useradmin/vite.config.js ./
COPY --from=build /app/admins/grifirio.useradmin/dist ./dist

EXPOSE 59264

CMD ["npm", "run", "preview", "--", "--host", "0.0.0.0", "--port", "59264"]
