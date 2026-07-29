FROM python:3.11-slim

WORKDIR /app

# System dependencies
RUN apt-get update && apt-get install -y \
    gcc \
    postgresql-client \
    && rm -rf /var/lib/apt/lists/*

# Python dependencies
COPY requirements.txt .
RUN pip install --no-cache-dir -r requirements.txt

# Copy application code
COPY . .

RUN chmod +x /app/entrypoint.sh

# Expose ports
EXPOSE 8000

# Migrations + RabbitMQ tuketicisi + ASGI sunucusu (bkz. entrypoint.sh)
CMD ["/app/entrypoint.sh"]
