# Build stage
FROM node:20-alpine AS build
WORKDIR /app

# Vite degiskenleri derleme aninda gomulur ve .env gitignore'da oldugu icin
# CI'da o dosya hic bulunmaz. Varsayilanlar burada durmazsa bundle bos bir
# gateway adresiyle derlenir ve panel hicbir API'ye ulasamaz.
ARG VITE_GATEWAY_URL=https://gateway.proudcoast-6064711b.westeurope.azurecontainerapps.io
ARG VITE_KEYCLOAK_URL=https://keycloak.proudcoast-6064711b.westeurope.azurecontainerapps.io
ARG VITE_KEYCLOAK_REALM=grafirio
ARG VITE_KEYCLOAK_CLIENT_ID=grafirio-client

ENV VITE_GATEWAY_URL=$VITE_GATEWAY_URL
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

# Copy built app from build stage
COPY --from=build /app/dist ./dist

# Expose port
EXPOSE 59265

# Start the app
CMD ["npm", "run", "preview", "--", "--host", "0.0.0.0", "--port", "59265"]
