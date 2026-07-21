"""
Celery Tasks for AI Processing

Soru isleme akisi tasks/executor.py'ye tasindi (Planner -> Executor -> Composer).
Bu modul DB yardimcilarini ve mevcut import yollarini (facade) korur:
messaging/consumers.py `tasks.ai_tasks._process_question`'i cagirmaya devam eder.
"""
from celery import shared_task
from datetime import datetime
import logging
import os
import re

logger = logging.getLogger(__name__)

# ──────────────────────────────────────────────
# Sabitler
# ──────────────────────────────────────────────
CHART_COLORS = [
    'rgba(124,58,237,0.8)',
    'rgba(16,185,129,0.8)',
    'rgba(245,158,11,0.8)',
    'rgba(239,68,68,0.8)',
    'rgba(59,130,246,0.8)',
    'rgba(236,72,153,0.8)',
    'rgba(99,102,241,0.8)',
    'rgba(6,182,212,0.8)',
]

DB_SCHEMA = """
Veritabani: GrafirioECommerce (SQL Server / T-SQL sozdizimi)
Tablolar:
  Categories(Id INT PK, Name NVARCHAR(100), ParentCategoryId INT nullable, IsActive BIT, DisplayOrder INT, CreatedDate DATETIME2)
  Products(Id INT PK, Name NVARCHAR, SKU NVARCHAR, CategoryId INT FK, Price DECIMAL(18,2), CostPrice DECIMAL, Brand NVARCHAR, Rating DECIMAL, ReviewCount INT, Stock INT, IsActive BIT, IsFeatured BIT, CreatedDate DATETIME2, UpdatedDate DATETIME2)
  Customers(Id INT PK, Email NVARCHAR, FirstName NVARCHAR, LastName NVARCHAR, Phone NVARCHAR, BirthDate DATETIME2, Gender NVARCHAR, CustomerType NVARCHAR, Country NVARCHAR, City NVARCHAR, TotalOrderCount INT, TotalSpent DECIMAL, RegistrationDate DATETIME2, LastLoginDate DATETIME2, IsActive BIT)
  Orders(Id INT PK, CustomerId INT FK, OrderNumber NVARCHAR, OrderDate DATETIME2, SubTotal DECIMAL, DiscountAmount DECIMAL, TaxAmount DECIMAL, ShippingCost DECIMAL, TotalAmount DECIMAL, Status NVARCHAR, PaymentMethod NVARCHAR, PaymentStatus NVARCHAR, ShippedDate DATETIME2, DeliveredDate DATETIME2)
  OrderItems(Id INT PK, OrderId INT FK, ProductId INT FK, ProductName NVARCHAR, Quantity INT, UnitPrice DECIMAL, DiscountAmount DECIMAL, TotalPrice DECIMAL)
  Addresses(Id INT PK, CustomerId INT FK, AddressType NVARCHAR, Country NVARCHAR, City NVARCHAR, District NVARCHAR, Street NVARCHAR, PostalCode NVARCHAR, IsDefault BIT)
  Reviews(Id INT PK, ProductId INT FK, CustomerId INT FK, Rating INT, Comment NVARCHAR, CreatedDate DATETIME2)
"""


# ──────────────────────────────────────────────
# Veritabani yardimcilari
# ──────────────────────────────────────────────
def get_db_connection():
    import pymssql
    host     = os.getenv('MSSQL_HOST', 'sqlserver.db.order')
    port     = int(os.getenv('MSSQL_PORT', '1433'))
    db_name  = os.getenv('MSSQL_DB', 'GrafirioECommerce')
    user     = os.getenv('MSSQL_USER', 'sa')
    password = os.getenv('MSSQL_PASSWORD', '')
    return pymssql.connect(server=host, port=port, database=db_name,
                           user=user, password=password, timeout=20)


def _assert_safe_select(sql: str) -> str:
    """Savunma katmani: pydantic SqlChartSpec dogrulamasinin arkasinda ikinci bir
    kontrol. Tek statement, SELECT/WITH disi anahtar kelime veya yorum yok."""
    stripped = sql.strip()
    body = stripped.rstrip(';').strip()
    upper = body.upper()

    if not (upper.startswith('SELECT') or upper.startswith('WITH')):
        raise ValueError("Yalnizca SELECT/WITH sorgulari calistirilabilir.")
    if ';' in body:
        raise ValueError("Tek statement disinda noktali virgul iceren sorgu reddedildi.")
    if '--' in body or '/*' in body:
        raise ValueError("Yorum satiri iceren sorgu reddedildi.")

    forbidden = ('INSERT', 'UPDATE', 'DELETE', 'DROP', 'ALTER', 'TRUNCATE',
                 'EXEC', 'EXECUTE', 'MERGE', 'GRANT', 'REVOKE', 'CREATE')
    for kw in forbidden:
        if re.search(rf'\b{kw}\b', upper):
            raise ValueError(f"'{kw}' iceren sorgu reddedildi.")
    return body


def execute_sql(sql: str):
    sql_stripped = _assert_safe_select(sql)
    conn = get_db_connection()
    try:
        cursor = conn.cursor()
        cursor.execute(sql)
        if cursor.description is None:
            return [], []
        columns = [d[0] for d in cursor.description]
        rows = [list(r) for r in cursor.fetchmany(100)]
        return columns, rows
    finally:
        conn.close()


def rows_to_chartjs(columns, rows, y_label='Deger'):
    labels = [str(r[0]) for r in rows]
    datasets = []
    for i, col in enumerate(columns[1:]):
        values = []
        for row in rows:
            val = row[i + 1]
            try:
                values.append(round(float(val), 2) if val is not None else 0)
            except (TypeError, ValueError):
                values.append(0)
        color = CHART_COLORS[i % len(CHART_COLORS)]
        datasets.append({
            'label': col or y_label,
            'data': values,
            'backgroundColor': color,
            'borderColor': color.replace('0.8', '1'),
            'borderWidth': 1,
        })
    return {'labels': labels, 'datasets': datasets}


# ──────────────────────────────────────────────
# Soru isleme (facade — gercek akis tasks/executor.py'de)
# ──────────────────────────────────────────────
def _process_question(message: dict):
    from tasks.executor import process_question
    process_question(message)


def _process_graph_data(message: dict):
    request_id = message.get('request_id', 'unknown')
    logger.info(f"Processing graph data request: {request_id}")
    from messaging.publishers import rabbitmq_client
    rabbitmq_client.publish_message(
        exchange='ai.responses',
        routing_key='ai.response.graph',
        message={
            'request_id': request_id,
            'user_id': message.get('user_id'),
            'success': True,
            'data': {'graph_type': 'bar', 'values': [], 'labels': []},
            'response_time': datetime.utcnow().isoformat(),
        },
    )


@shared_task(bind=True, max_retries=3)
def process_question_request(self, message):
    """Celery wrapper."""
    try:
        _process_question(message)
        return {'request_id': message.get('request_id'), 'success': True}
    except Exception as exc:
        logger.error("Task hatasi | RequestId=%s | %s", message.get('request_id'), exc)
        raise self.retry(exc=exc, countdown=30)


@shared_task(bind=True, max_retries=3)
def process_graph_data_request(self, message):
    """Celery wrapper."""
    try:
        _process_graph_data(message)
        return {'request_id': message.get('request_id'), 'success': True}
    except Exception as exc:
        logger.error(f"Error processing graph data request: {exc}")
        raise self.retry(exc=exc, countdown=60)
