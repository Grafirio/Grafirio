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


def _quote_ident(name: str) -> str:
    """INFORMATION_SCHEMA sorgusuna gomulecek tablo adlarini guvenli hale getirir."""
    return "'" + str(name).replace("'", "''") + "'"


_schema_cache: dict = {}
_table_list_cache: dict = {}


def list_table_names() -> list:
    """Baglanilan veritabanindaki temel tablolarin 'schema.tablo' adlari."""
    cache_key = (os.getenv('MSSQL_HOST', ''), os.getenv('MSSQL_DB', ''))
    if cache_key in _table_list_cache:
        return _table_list_cache[cache_key]

    conn = get_db_connection()
    try:
        cursor = conn.cursor()
        cursor.execute(
            "SELECT TABLE_SCHEMA, TABLE_NAME FROM INFORMATION_SCHEMA.TABLES "
            "WHERE TABLE_TYPE = 'BASE TABLE' ORDER BY TABLE_SCHEMA, TABLE_NAME"
        )
        names = [f"{s}.{t}" for s, t in cursor.fetchall()]
    finally:
        conn.close()

    _table_list_cache[cache_key] = names
    return names


def build_db_schema(selected_tables=None) -> str:
    """
    Baglanilan veritabaninin gercek semasini prompt'a uygun metne cevirir.

    Sabit bir sema metni yalnizca demo veritabanini tarif ediyordu; musteri
    veritabanina baglanildiginda model olmayan tablolari uyduruyor ve sorgular
    "Invalid object name" ile dusuyordu.

    Kullanicinin sectigi tablolar varsa yalnizca onlarin kolonlari verilir —
    bir semada yuzlerce tablo olabildigi icin hepsini gondermek hem token
    israfi hem de modelin dikkatini dagitiyor. Secim yoksa tablo adlari
    listelenir ki model en azindan var olan adlar arasindan secsin.
    """
    host = os.getenv('MSSQL_HOST', '')
    db_name = os.getenv('MSSQL_DB', '')
    names = tuple(sorted(str(t) for t in (selected_tables or [])))
    cache_key = (host, db_name, names)
    if cache_key in _schema_cache:
        return _schema_cache[cache_key]

    conn = get_db_connection()
    try:
        cursor = conn.cursor()
        if names:
            # "dbo.Foo" ya da "Foo" olarak gelebilir — ikisini de kabul et.
            bare = {n.split('.')[-1] for n in names}
            in_list = ', '.join(_quote_ident(n) for n in sorted(bare))
            cursor.execute(
                "SELECT TABLE_SCHEMA, TABLE_NAME, COLUMN_NAME, DATA_TYPE, IS_NULLABLE "
                "FROM INFORMATION_SCHEMA.COLUMNS "
                f"WHERE TABLE_NAME IN ({in_list}) "
                "ORDER BY TABLE_SCHEMA, TABLE_NAME, ORDINAL_POSITION"
            )
            grouped: dict = {}
            for schema, table, column, dtype, nullable in cursor.fetchall():
                col = f"{column} {dtype}" + ("" if nullable == 'NO' else " NULL")
                grouped.setdefault(f"{schema}.{table}", []).append(col)
            lines = [f"  {t}({', '.join(cols)})" for t, cols in grouped.items()]
            detail = "Tablolar ve kolonlari:"
        else:
            cursor.execute(
                "SELECT TABLE_SCHEMA, TABLE_NAME FROM INFORMATION_SCHEMA.TABLES "
                "WHERE TABLE_TYPE = 'BASE TABLE' ORDER BY TABLE_SCHEMA, TABLE_NAME"
            )
            all_tables = [f"{s}.{t}" for s, t in cursor.fetchall()]
            capped = all_tables[:200]
            lines = ["  " + ", ".join(capped)]
            detail = f"Tablolar (toplam {len(all_tables)}, ilk {len(capped)} tanesi):"
            if len(all_tables) > len(capped):
                lines.append("  ... liste kirpildi; kolon detayi icin tablo secimi yapilmali.")
    finally:
        conn.close()

    if not lines:
        raise RuntimeError("Sema okunamadi: INFORMATION_SCHEMA bos dondu")

    schema_text = (
        f"\nVeritabani: {db_name} (SQL Server / T-SQL sozdizimi)\n"
        f"{detail}\n" + "\n".join(lines) +
        "\nYalnizca yukarida listelenen tablo ve kolonlari kullan; "
        "listede olmayan bir ad uydurma.\n"
    )
    _schema_cache[cache_key] = schema_text
    return schema_text


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
