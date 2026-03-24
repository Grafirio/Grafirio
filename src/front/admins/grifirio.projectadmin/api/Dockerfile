FROM node:20-alpine

# Install Docker CLI
RUN apk add --no-cache docker-cli

WORKDIR /app

# Copy package files
COPY package*.json ./

# Install dependencies
RUN npm ci --production

# Copy server code
COPY server.js ./

# Expose port
EXPOSE 3001

# Start backend
CMD ["node", "server.js"]
