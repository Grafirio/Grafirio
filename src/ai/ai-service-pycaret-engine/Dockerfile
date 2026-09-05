FROM python:3.11-slim-bookworm

WORKDIR /app

# System dependencies
RUN apt-get update && apt-get install -y \
    gcc \
    g++ \
    gfortran \
    libgomp1 \
    curl \
    gnupg \
    && rm -rf /var/lib/apt/lists/*

# MS SQL Server ODBC surucusu.
# requirements.txt'te pyodbc vardi ama pyodbc yalnizca bir sarmalayici;
# native surucu olmadan "Can't open lib 'ODBC Driver ...' : file not found"
# ile duser. Debian 12 (bookworm) icin Microsoft artik 17 degil 18
# dagitiyor — main.py'deki DSN de 18'i istiyor, ikisi birlikte degismeli.
RUN curl -sSL https://packages.microsoft.com/keys/microsoft.asc \
      | gpg --dearmor -o /usr/share/keyrings/microsoft-prod.gpg \
 && echo "deb [signed-by=/usr/share/keyrings/microsoft-prod.gpg] https://packages.microsoft.com/debian/12/prod bookworm main" \
      > /etc/apt/sources.list.d/mssql-release.list \
 && apt-get update \
 && ACCEPT_EULA=Y apt-get install -y msodbcsql18 unixodbc \
 && rm -rf /var/lib/apt/lists/*

# Python dependencies
COPY requirements.txt .
RUN pip install --no-cache-dir -r requirements.txt

# Create models directory
RUN mkdir -p /app/models /app/data

ENV AGENT_JOB_DB_PATH=/app/data/agent-jobs.sqlite3
VOLUME ["/app/data"]

# Copy application code
COPY . .

# Expose port
EXPOSE 8002

# Run FastAPI
CMD ["uvicorn", "main:app", "--host", "0.0.0.0", "--port", "8002"]
