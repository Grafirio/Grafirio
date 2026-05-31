"""
Celery Tasks for AI Processing
"""
from celery import shared_task
from datetime import datetime
import logging
import json
import os
import re

logger = logging.getLogger(__name__)

# ──────────────────────────────────────────────
# Sabitler
# ──────────────────────────────────────────────
GEMINI_MODEL = os.getenv('GEMINI_MODEL', 'gemini-2.0-flash')

CHART_KEYWORDS = [
    # ── Genel tetikleyiciler ───────────────────────────────────────────
    'grafik', 'chart', 'gorselleştir', 'gorsel olarak', 'diyagram', 'diagram',
    'grafikle', 'grafigini', 'grafigi', 'goster', 'görsel', 'ciz', 'olustur',
    'grafik olarak', 'olarak goster',
    # ── Bar / Sütun ───────────────────────────────────────────────────
    'bar', 'bar chart', 'bar grafik',
    'cubuk', 'cubuk grafik', 'sutun', 'sutun grafik', 'kolon',
    'dikey grafik', 'yatay grafik',
    'stacked bar', 'grouped bar', 'yigil',
    # ── Çizgi / Alan / Trend ─────────────────────────────────────────
    'cizgi', 'cizgi grafik', 'line', 'line chart',
    'trend', 'zaman serisi', 'time series',
    'alan', 'alan grafik', 'area', 'area chart',
    'stacked area', 'spline', 'adim grafik',
    # ── Pasta / Halka ─────────────────────────────────────────────────
    'pasta', 'pasta grafik', 'pie', 'pie chart',
    'halka', 'halka grafik', 'doughnut', 'donut',
    'dilim', 'oran grafigi', 'yuzde grafigi', 'yuzde dagilimi',
    'oranlar', 'yuzde',
    # ── Dağılım / Nokta / Bubble ──────────────────────────────────────
    'dagilim', 'dagilim grafigi', 'scatter', 'scatter plot',
    'nokta grafik', 'bubble', 'balon', 'korelasyon', 'correlation',
    # ── Histogram ─────────────────────────────────────────────────────
    'histogram', 'frekans grafigi', 'frekans dagilimi',
    # ── Radar / Örümcek ───────────────────────────────────────────────
    'radar', 'radar grafik', 'spider', 'orumcek', 'polar',
    # ── Isı Haritası ─────────────────────────────────────────────────
    'isi haritasi', 'heatmap', 'heat map',
    # ── Kutu / İstatistik ─────────────────────────────────────────────
    'kutu grafik', 'box plot', 'box-whisker', 'violin plot',
    # ── Huni ─────────────────────────────────────────────────────────
    'huni', 'huni grafik', 'funnel', 'funnel chart',
    'satis hunisi', 'donusum hunisi',
    # ── Şelale / Waterfall ────────────────────────────────────────────
    'selale', 'selale grafik', 'waterfall', 'kumulatif grafik',
    # ── Treemap ───────────────────────────────────────────────────────
    'treemap', 'agac haritasi', 'hiyerarsik grafik',
    # ── Pareto ────────────────────────────────────────────────────────
    'pareto', 'pareto grafik', 'pareto analizi',
    # ── Finansal ─────────────────────────────────────────────────────
    'mum grafik', 'candlestick', 'ohlc',
    # ── İstatistik genel ─────────────────────────────────────────────
    'istatistik', 'istatistiksel', 'analiz grafik', 'dagilimini goster',
    # ── UTF-8 versiyonları ────────────────────────────────────────────
    'grafik', 'chart', 'görselleştir', 'görsel olarak',
    'çubuk', 'sütun', 'çizgi', 'çizgi grafiği',
    'pasta grafiği', 'halka grafiği', 'dağılım', 'dağılım grafiği',
    'ısı haritası', 'kutu grafiği', 'huni grafiği', 'şelale grafiği',
    'ağaç haritası', 'karşılaştır', 'karşılaştırmalı',
    'trend analizi', 'zaman çizelgesi', 'yüzde dağılımı',
    'grafiğini', 'grafiği', 'göster', 'çiz', 'oluştur',
    'bar grafiği', 'sütun grafiği', 'alan grafiği',
]

PREDICTIVE_KEYWORDS = [
    'tahmin', 'forecast', 'prediction', 'predict', 'beklenen', 'gelecek',
    'sonraki', 'onumuzdeki', 'önümüzdeki', 'olasilik', 'olasılık', 'risk',
    'churn', 'terk', 'anomali', 'anomaly', 'senaryo', 'what if',
]

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

SQL_GEN_SYSTEM = (
    "Sen bir SQL uzmanisın. Kullanicinin sorusunu analiz edip T-SQL (SQL Server) SELECT sorgusu uretiyorsun. "
    "SADECE asagidaki JSON formatinda yanit ver, baska hicbir sey yazma:\n"
    '{"sql":"<SELECT sorgusu>","chart_type":"<bar|line|pie|doughnut>","title":"<baslik>","x_label":"<X ekseni etiketi>","y_label":"<Y ekseni etiketi>"}'
)

TEXT_SYSTEM_PROMPT = (
    "Sen Grifirio adli bir Is Zekasi (BI) platformunun AI asistanisin. "
    "Kullanicilarin veri analizi, istatistik ve is zekasi sorularini yanitliyorsun. "
    "Yanitlarin kisa, net ve uygulanabilir olsun. "
    "Turkce sorulara Turkce, Ingilizce sorulara Ingilizce yanit ver."
)


def is_chart_request(question: str) -> bool:
    q = question.lower()
    return any(kw in q for kw in CHART_KEYWORDS)


def is_predictive_request(question: str) -> bool:
    q = question.lower()
    return any(kw in q for kw in PREDICTIVE_KEYWORDS)


def classify_question_intent(question: str) -> str:
    # Predictive intent has higher priority than chart intent.
    if is_predictive_request(question):
        return 'predictive'
    if is_chart_request(question):
        return 'chart'
    return 'text'


def get_db_connection():
    import pymssql
    host     = os.getenv('MSSQL_HOST', 'sqlserver.db.order')
    port     = int(os.getenv('MSSQL_PORT', '1433'))
    db_name  = os.getenv('MSSQL_DB', 'GrafirioECommerce')
    user     = os.getenv('MSSQL_USER', 'sa')
    password = os.getenv('MSSQL_PASSWORD', '')
    return pymssql.connect(server=host, port=port, database=db_name,
                           user=user, password=password, timeout=20)


def execute_sql(sql: str):
    sql_stripped = sql.strip().upper()
    if not sql_stripped.startswith('SELECT') and not sql_stripped.startswith('WITH'):
        raise ValueError("Yalnizca SELECT/WITH sorgulari calistirilamalri.")
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


def extract_json(text: str) -> dict:
    cleaned = re.sub(r'```(?:json)?', '', text).strip().strip('`').strip()
    match = re.search(r'\{.*\}', cleaned, re.DOTALL)
    if match:
        return json.loads(match.group())
    raise ValueError(f"JSON bulunamadi: {text[:200]}")


def _process_question(message: dict):
    import time
    import requests as http_requests
    import google.generativeai as genai
    from django.conf import settings

    request_id = message.get('request_id', '')
    question   = message.get('question', '').strip()
    context    = message.get('context', [])
    intent     = classify_question_intent(question)

    logger.info(
        "Soru alindi | RequestId=%s | Intent=%s | Grafik=%s | Predictive=%s | Soru=%s",
        request_id,
        intent,
        is_chart_request(question),
        is_predictive_request(question),
        question,
    )

    csharp_api_url = (
        getattr(settings, 'CSHARP_API_URL', None)
        or os.getenv('CSHARP_API_URL', 'http://localhost:5221')
    )

    def post_result(status, result_obj):
        payload = {
            'requestId':   request_id,
            'status':      status,
            'result':      json.dumps(result_obj, ensure_ascii=False),
            'completedAt': datetime.utcnow().isoformat(),
        }
        try:
            resp = http_requests.post(
                f"{csharp_api_url}/api/ai/query-result",
                json=payload,
                timeout=10,
            )
            logger.info("Sonuc gonderildi | RequestId=%s | status=%s | HTTP=%s",
                        request_id, status, resp.status_code)
        except Exception as pe:
            logger.error("C# API POST hatasi | %s", pe)

    def try_pycaret_predict() -> tuple[bool, str]:
        """
        Attempt to route predictive intent to PyCaret engine.
        Expected message shape (at least):
          company_id, and either
          - predict_request: { table_name: str, data: dict }
          - or table_name + predict_data at top level.
        """
        pycaret_base_url = (
            os.getenv('PYCARET_ENGINE_URL')
            or os.getenv('AI_SERVICE_1_URL')
            or ''
        ).rstrip('/')

        if not pycaret_base_url:
            return False, 'PYCARET_ENGINE_URL tanimli degil'

        company_id = message.get('company_id', '')
        req = message.get('predict_request') or {}
        table_name = req.get('table_name') or message.get('table_name')
        predict_data = req.get('data') or message.get('predict_data')

        if not company_id or not table_name or not isinstance(predict_data, dict):
            return False, 'PyCaret icin gerekli predict payload eksik'

        payload = {
            'company_id': company_id,
            'table_name': table_name,
            'data': predict_data,
        }

        try:
            resp = http_requests.post(
                f"{pycaret_base_url}/predict",
                json=payload,
                timeout=20,
            )
            if resp.status_code >= 400:
                return False, f'PyCaret HTTP {resp.status_code}: {resp.text[:120]}'

            pred_body = resp.json()
            post_result('completed', {
                'type': 'predictive',
                'success': True,
                'question': question,
                'answer': 'PyCaret tahmin sonucu olusturuldu.',
                'charts': [],
                'predictions': pred_body.get('predictions', pred_body),
                'answeredAt': datetime.utcnow().isoformat(),
            })
            return True, ''
        except Exception as ex:
            return False, str(ex)

    api_key = getattr(settings, 'GEMINI_API_KEY', '') or os.getenv('GEMINI_API_KEY', '')
    if not api_key:
        raise RuntimeError("GEMINI_API_KEY yapilandirilmamis")

    genai.configure(api_key=api_key)

    def gemini_generate(model, prompt_or_fn, max_retries=1):
        """Rate limit (429) gelince hemen hata firlat — frontend 90s'de timeout'a ugrar,
        kullaniciya anlik rate-limit mesaji gonderilsin, 1 dakika sonra tekrar denesin."""
        for attempt in range(max_retries):
            try:
                if callable(prompt_or_fn):
                    return prompt_or_fn()
                return model.generate_content(prompt_or_fn)
            except Exception as e:
                err = str(e)
                if '429' in err or 'quota' in err.lower() or 'rate' in err.lower():
                    m = re.search(r'seconds:\s*(\d+)', err)
                    wait_secs = int(m.group(1)) + 2 if m else 60
                    logger.warning("Rate limit — aninda hata bildiriliyor (bekleme: %ds)", wait_secs)
                    # Rate limit icin ozel exception olustur — consumers.py bunu yakalar
                    raise RuntimeError(f"RATE_LIMIT:{wait_secs}")
                raise

    force_chart_fallback = False
    if intent == 'predictive':
        routed, route_err = try_pycaret_predict()
        if routed:
            logger.info("Predictive intent PyCaret'e yonlendirildi | RequestId=%s", request_id)
            return

        logger.warning(
            "Predictive intent fallback | RequestId=%s | Sebep=%s",
            request_id,
            route_err,
        )

        # If user also explicitly asked for a chart, fallback to SQL chart path.
        if is_chart_request(question):
            force_chart_fallback = True
        else:
            text_model = genai.GenerativeModel(
                model_name=GEMINI_MODEL,
                system_instruction=TEXT_SYSTEM_PROMPT,
            )
            fb_prompt = (
                f"Kullanici sorusu: {question}\n"
                "Bu soru predictive niyet tasiyor ancak model/feature payload hazir degil. "
                "Kullaniciyi kisa ve net bicimde bilgilendir: once model egitimi ve gerekli alanlarin secimi gerektigini soyle, "
                "ardindan ayni soru icin su an hangi tarihsel grafiklerin gosterilebilecegine 1-2 ornek ver."
            )
            response = gemini_generate(text_model, fb_prompt)
            post_result('completed', {
                'type': 'text',
                'success': True,
                'question': question,
                'answer': response.text,
                'charts': [],
                'answeredAt': datetime.utcnow().isoformat(),
            })
            return

    if intent == 'chart' or force_chart_fallback:
        logger.info("Grafik istegi tespit edildi")
        sql_gen_model = genai.GenerativeModel(
            model_name=GEMINI_MODEL,
            system_instruction=SQL_GEN_SYSTEM,
        )
        sql_prompt = (
            f"{DB_SCHEMA}\n\nKullanici sorusu: {question}\n\n"
            "Yukaridaki semaya uygun bir T-SQL SELECT sorgusu ve grafik metadatasi uret. Sadece JSON dondur."
        )
        sql_resp = gemini_generate(sql_gen_model, sql_prompt)
        sql_meta = extract_json(sql_resp.text)

        sql        = sql_meta.get('sql', '')
        chart_type = sql_meta.get('chart_type', 'bar').lower()
        title      = sql_meta.get('title', question[:60])
        y_label    = sql_meta.get('y_label', 'Deger')

        logger.info("Uretilen SQL | %s", sql[:200])

        try:
            columns, rows = execute_sql(sql)
            logger.info("SQL calistirildi | %d satir", len(rows))
        except Exception as db_err:
            logger.warning("SQL calistirilamadi, ornek veri isteniyor | %s", db_err)
            fallback_model = genai.GenerativeModel(
                model_name=GEMINI_MODEL,
                system_instruction=TEXT_SYSTEM_PROMPT,
            )
            fb_prompt = (
                f"{question} -- Veritabanina su an ulasilamiyor. "
                "Bu analiz icin gercekci ornek veriler olustur ve yaniti SADECE asagidaki JSON formatinda ver:\n"
                '{"labels":["..."],"datasets":[{"label":"...","data":[...]}]}'
            )
            fb_resp = gemini_generate(fallback_model, fb_prompt)
            fb_data = extract_json(fb_resp.text)
            post_result('completed', {
                'type': 'chart', 'chartType': chart_type, 'title': title,
                'success': True,
                'answer': f'Ornek veri gosteriliyor (DB baglantisi yok): {str(db_err)[:80]}',
                'charts': [{'type': chart_type, 'title': title, 'data': fb_data}],
                'answeredAt': datetime.utcnow().isoformat(),
            })
            return

        if not rows:
            logger.warning("Sorgu bos dondu, genis aralikla retry | SQL=%s", sql[:200])
            retry_prompt = (
                f"{DB_SCHEMA}\n\nKullanici sorusu: {question}\n\n"
                "Onceki sorgu hic sonuc dondurmedi (muhtemelen tarih filtresi cok dar). "
                "Bu kez tarih filtresini KALDIR veya cok daha genis bir tarih araligi kullan (orn. son 2 yil). "
                "Sadece JSON dondur."
            )
            retry_resp = gemini_generate(sql_gen_model, retry_prompt)
            retry_meta = extract_json(retry_resp.text)
            retry_sql  = retry_meta.get('sql', '')
            logger.info("Retry SQL | %s", retry_sql[:200])
            try:
                columns, rows = execute_sql(retry_sql)
            except Exception as retry_err:
                logger.error("Retry SQL de basarisiz | %s", retry_err)
                rows = []

        if not rows:
            fallback_model2 = genai.GenerativeModel(
                model_name=GEMINI_MODEL,
                system_instruction=TEXT_SYSTEM_PROMPT,
            )
            fb2_prompt = (
                f"{question} -- Veritabaninda bu soruya uygun veri bulunamadi. "
                "Bu analiz icin gercekci ornek veriler olustur ve yaniti SADECE asagidaki JSON formatinda ver:\n"
                '{"labels":["..."],"datasets":[{"label":"...","data":[...]}]}'
            )
            fb2_resp = gemini_generate(fallback_model2, fb2_prompt)
            fb2_data = extract_json(fb2_resp.text)
            post_result('completed', {
                'type': 'chart', 'chartType': chart_type, 'title': title,
                'success': True,
                'answer': 'Veritabaninda bu donem icin veri bulunamadi. Ornek veri gosteriliyor.',
                'charts': [{'type': chart_type, 'title': title, 'data': fb2_data}],
                'answeredAt': datetime.utcnow().isoformat(),
            })
            return

        chart_data = rows_to_chartjs(columns, rows, y_label)
        post_result('completed', {
            'type': 'chart', 'chartType': chart_type, 'title': title,
            'success': True,
            'answer': f'"{title}" grafigi olusturuldu ({len(rows)} veri noktasi).',
            'charts': [{'type': chart_type, 'title': title, 'data': chart_data}],
            'answeredAt': datetime.utcnow().isoformat(),
        })

    else:
        text_model = genai.GenerativeModel(
            model_name=GEMINI_MODEL,
            system_instruction=TEXT_SYSTEM_PROMPT,
        )
        history = []
        for item in context:
            try:
                parsed = json.loads(item) if isinstance(item, str) else item
                role    = parsed.get('role', 'user')
                content = parsed.get('content', '')
                if role in ('user', 'model') and content:
                    history.append({'role': role, 'parts': [{'text': content}]})
            except (json.JSONDecodeError, AttributeError):
                pass

        chat     = text_model.start_chat(history=history)
        response = gemini_generate(text_model, lambda: chat.send_message(question))
        answer   = response.text

        logger.info("Gemini metin yaniti | %d karakter", len(answer))

        post_result('completed', {
            'type': 'text', 'success': True,
            'question': question, 'answer': answer,
            'charts': [], 'answeredAt': datetime.utcnow().isoformat(),
        })


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
