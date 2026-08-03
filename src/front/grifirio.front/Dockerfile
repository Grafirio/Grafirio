# Build stage
FROM node:20-alpine AS build
WORKDIR /app

# Vitrinin bildigi tek adres, "giris yap" ve "basla" dugmelerinin gittigi urun
# uygulamasi. Vite degiskenleri derleme aninda gomuldugu ve .env gitignore'da
# oldugu icin varsayilan burada duruyor; olmazsa dugmeler bos bir adrese gider.
ARG VITE_APP_URL=https://admin.grafirio.com
ENV VITE_APP_URL=$VITE_APP_URL

# "Ucretsiz basla" Keycloak'in kayit ekranina dogrudan gidiyor; uygulamaya
# ugramak kullaniciyi giris ekranina dusuruyordu (bkz. src/config.js).
ARG VITE_KEYCLOAK_URL=https://login.grafirio.com
ARG VITE_KEYCLOAK_REALM=grafirio
ARG VITE_KEYCLOAK_CLIENT_ID=grafirio-client

ENV VITE_KEYCLOAK_URL=$VITE_KEYCLOAK_URL
ENV VITE_KEYCLOAK_REALM=$VITE_KEYCLOAK_REALM
ENV VITE_KEYCLOAK_CLIENT_ID=$VITE_KEYCLOAK_CLIENT_ID

# Copy package files
COPY package*.json ./

# Install dependencies
RUN npm ci

# Copy source code
COPY . .

# Build the app
RUN npm run build

# Production stage
FROM node:20-alpine AS production
WORKDIR /app

# Copy package files and node_modules from build stage
COPY --from=build /app/package*.json ./
COPY --from=build /app/node_modules ./node_modules
# "vite preview" bu dosyayi calisma aninda okur; olmadan varsayilanlara doner
# ve tanimadigi Host basliklarini 403 ile reddeder.
COPY --from=build /app/vite.config.js ./

# Copy built app from build stage
COPY --from=build /app/dist ./dist

# Expose port
EXPOSE 59264

# Start the app
CMD ["npm", "run", "preview", "--", "--host", "0.0.0.0", "--port", "59264"]
