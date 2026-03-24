# Build stage
FROM node:20-alpine AS build
WORKDIR /app

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
EXPOSE 59266

# Start the app
CMD ["npm", "run", "preview", "--", "--host", "0.0.0.0", "--port", "59266"]
