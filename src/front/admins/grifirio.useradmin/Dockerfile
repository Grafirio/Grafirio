# Build stage
FROM node:20-alpine AS build
WORKDIR /app

# Add Build Arguments for Vite Environment Variables
ARG VITE_KEYCLOAK_URL=https://keycloak.proudcoast-6064711b.westeurope.azurecontainerapps.io
ARG VITE_KEYCLOAK_REALM=grafirio
ARG VITE_KEYCLOAK_CLIENT_ID=grafirio-client
ARG VITE_API_GATEWAY_URL=https://gateway.proudcoast-6064711b.westeurope.azurecontainerapps.io
# The code reads VITE_API_URL, not VITE_API_GATEWAY_URL. Without it the bundle
# fell back to http://localhost:5000 and every data-analysis call left the
# browser aimed at the user's own machine.
ARG VITE_API_URL=https://gateway.proudcoast-6064711b.westeurope.azurecontainerapps.io

# Expose build args as Environment Variables so Vite build reads them
ENV VITE_KEYCLOAK_URL=$VITE_KEYCLOAK_URL
ENV VITE_KEYCLOAK_REALM=$VITE_KEYCLOAK_REALM
ENV VITE_KEYCLOAK_CLIENT_ID=$VITE_KEYCLOAK_CLIENT_ID
ENV VITE_API_GATEWAY_URL=$VITE_API_GATEWAY_URL
ENV VITE_API_URL=$VITE_API_URL

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
