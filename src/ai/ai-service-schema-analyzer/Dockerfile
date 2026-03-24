FROM python:3.11-slim

WORKDIR /app

# System dependencies for pymssql and other packages
RUN apt-get update && apt-get install -y \
    gcc \
    g++ \
    freetds-dev \
    freetds-bin \
    supervisor \
    && rm -rf /var/lib/apt/lists/*

# Python dependencies
COPY requirements.txt .
RUN pip install --no-cache-dir -r requirements.txt

# Copy application code
COPY . .

# Create supervisor config
RUN echo "[supervisord]" > /etc/supervisor/conf.d/supervisord.conf \
    && echo "nodaemon=true" >> /etc/supervisor/conf.d/supervisord.conf \
    && echo "" >> /etc/supervisor/conf.d/supervisord.conf \
    && echo "[program:fastapi]" >> /etc/supervisor/conf.d/supervisord.conf \
    && echo "command=uvicorn main:app --host 0.0.0.0 --port 8001" >> /etc/supervisor/conf.d/supervisord.conf \
    && echo "autostart=true" >> /etc/supervisor/conf.d/supervisord.conf \
    && echo "autorestart=true" >> /etc/supervisor/conf.d/supervisord.conf \
    && echo "stdout_logfile=/dev/stdout" >> /etc/supervisor/conf.d/supervisord.conf \
    && echo "stdout_logfile_maxbytes=0" >> /etc/supervisor/conf.d/supervisord.conf \
    && echo "stderr_logfile=/dev/stderr" >> /etc/supervisor/conf.d/supervisord.conf \
    && echo "stderr_logfile_maxbytes=0" >> /etc/supervisor/conf.d/supervisord.conf \
    && echo "" >> /etc/supervisor/conf.d/supervisord.conf \
    && echo "[program:rabbitmq_consumer]" >> /etc/supervisor/conf.d/supervisord.conf \
    && echo "command=python rabbitmq_consumer.py" >> /etc/supervisor/conf.d/supervisord.conf \
    && echo "autostart=true" >> /etc/supervisor/conf.d/supervisord.conf \
    && echo "autorestart=true" >> /etc/supervisor/conf.d/supervisord.conf \
    && echo "stdout_logfile=/dev/stdout" >> /etc/supervisor/conf.d/supervisord.conf \
    && echo "stdout_logfile_maxbytes=0" >> /etc/supervisor/conf.d/supervisord.conf \
    && echo "stderr_logfile=/dev/stderr" >> /etc/supervisor/conf.d/supervisord.conf \
    && echo "stderr_logfile_maxbytes=0" >> /etc/supervisor/conf.d/supervisord.conf \
    && echo "" >> /etc/supervisor/conf.d/supervisord.conf \
    && echo "[program:query_executor]" >> /etc/supervisor/conf.d/supervisord.conf \
    && echo "command=python query_executor.py" >> /etc/supervisor/conf.d/supervisord.conf \
    && echo "autostart=true" >> /etc/supervisor/conf.d/supervisord.conf \
    && echo "autorestart=true" >> /etc/supervisor/conf.d/supervisord.conf \
    && echo "stdout_logfile=/dev/stdout" >> /etc/supervisor/conf.d/supervisord.conf \
    && echo "stdout_logfile_maxbytes=0" >> /etc/supervisor/conf.d/supervisord.conf \
    && echo "stderr_logfile=/dev/stderr" >> /etc/supervisor/conf.d/supervisord.conf \
    && echo "stderr_logfile_maxbytes=0" >> /etc/supervisor/conf.d/supervisord.conf

# Expose port
EXPOSE 8001

# Run supervisor to manage both processes
CMD ["/usr/bin/supervisord", "-c", "/etc/supervisor/conf.d/supervisord.conf"]
