"""
Executor + Composer: planner'in urettigi gorevleri calistirir, sonuclari
tek bir 'composite' payload'da toplar ve C# API'ye postlar.

Akis: progress(5) -> plan -> gorev basina SQL uret/calistir (hata geribildirimli
retry) -> narrative en son (gercek hesaplanmis verilerle) -> composite post_result.

Sahte veri fallback'i YOKTUR: basarisiz gorev failedTasks'e durustce yazilir.
"""
import json
import logging
import os
from datetime import datetime

import requests as http_requests

from llm.client import LLMClient, LLMError
from llm.schemas import PlanTask, SqlChartSpec
from llm.structured import generate_structured
from tasks.planner import build_plan

logger = logging.getLogger(__name__)


class TaskFailure(Exception):
    """Tek bir gorevin kalici basarisizligi (pipeline devam eder)."""


SQL_GEN_SYSTEM = (
    "Sen bir SQL uzmanısın. Verilen görev için T-SQL (SQL Server) SELECT sorgusu üretiyorsun. "
    "SADECE aşağıdaki JSON formatında yanıt ver, başka hiçbir şey yazma:\n"
    '{"sql":"<SELECT sorgusu>","chart_type":"bar|line|area|pie|doughnut|radar|scatter",'
    '"title":"<başlık>","x_label":"<X ekseni>","y_label":"<Y ekseni>"}\n'
    "Kurallar: Tek bir SELECT (veya WITH) statement üret; noktalı virgül, yorum satırı, "
    "DML/DDL kullanma. İlk kolon etiket (X ekseni), sonraki kolonlar sayısal seri olsun. "
    "Satır sayısını makul tut (TOP N veya tarih aralığı)."
)

SERIES_SQL_SYSTEM = (
    "Sen bir SQL uzmanısın. Verilen görev için AYLIK zaman serisi döndüren tek bir "
    "T-SQL (SQL Server) SELECT sorgusu üretiyorsun. "
    "SADECE aşağıdaki JSON formatında yanıt ver:\n"
    '{"sql":"<SELECT sorgusu>","chart_type":"line","title":"<başlık>","x_label":"Ay","y_label":"<birim>"}\n'
    "Kurallar: Sorgu tam iki kolon döndürsün: FORMAT(<tarih>, 'yyyy-MM') AS Period ve "
    "SUM(...) AS Value. En az son 12 ayı kapsasın, Period'a göre sıralı olsun. "
    "Tek statement; noktalı virgül ve yorum yok."
)

NARRATIVE_SYSTEM = (
    "Sen Grafirio adlı bir İş Zekası (BI) platformunun AI asistanısın. "
    "Sana hesaplanmış GERÇEK veriler verilir; yalnızca bu verilere dayanarak yorum yaparsın. "
    "Veri uydurma, verilmemiş sayı kullanma. Yanıt kısa, net ve uygulanabilir olsun. "
    "Türkçe sorulara Türkçe, İngilizce sorulara İngilizce yanıt ver."
)


# ── C# API iletisimi ─────────────────────────────────────────────────────

def _csharp_api_url() -> str:
    try:
        from django.conf import settings
        url = getattr(settings, 'CSHARP_API_URL', None)
        if url:
            return url
    except Exception:
        pass
    return os.getenv('CSHARP_API_URL', 'http://localhost:5221')


def post_result(request_id: str, status: str, result_obj: dict):
    payload = {
        'requestId': request_id,
        'status': status,
        'result': json.dumps(result_obj, ensure_ascii=False),
        'completedAt': datetime.utcnow().isoformat(),
    }
    try:
        resp = http_requests.post(f"{_csharp_api_url()}/api/ai/query-result",
                                  json=payload, timeout=10)
        logger.info("Sonuc gonderildi | RequestId=%s | status=%s | HTTP=%s",
                    request_id, status, resp.status_code)
    except Exception as e:
        logger.error("C# API POST hatasi | RequestId=%s | %s", request_id, e)


def post_progress(request_id: str, progress: int, message: str):
    try:
        http_requests.post(
            f"{_csharp_api_url()}/api/ai/update-progress",
            json={'requestId': request_id, 'progress': int(progress),
                  'message': message, 'timestamp': datetime.utcnow().isoformat()},
            timeout=5,
        )
    except Exception as e:
        logger.warning("Progress POST hatasi (yutuldu) | %s", e)


# ── Gorev calistiricilar ─────────────────────────────────────────────────

def _generate_sql_spec(llm: LLMClient, task: PlanTask, db_schema: str,
                       system: str, extra: str = '') -> SqlChartSpec:
    messages = [{'role': 'user', 'content':
        f"{db_schema}\n\nGörev: {task.question_fragment}\n"
        f"İstenen grafik tipi: {task.chart_type}\nBaşlık önerisi: {task.title}\n"
        f"{extra}Sadece JSON döndür."}]
    return generate_structured(llm, system, messages, SqlChartSpec, max_tokens=1200)


def _execute_with_retry(llm: LLMClient, task: PlanTask, db_schema: str,
                        system: str) -> tuple:
    """SQL uret -> calistir. DB hatasinda hata mesajiyla 1 re-prompt,
    bos sonucta 1 tarih-genislet retry. Doner: (spec, columns, rows)."""
    from tasks.ai_tasks import execute_sql

    spec = _generate_sql_spec(llm, task, db_schema, system)
    logger.info("Uretilen SQL | %s | %s", task.title, spec.sql[:200])
    try:
        columns, rows = execute_sql(spec.sql)
    except Exception as db_err:
        logger.warning("SQL hatasi, re-prompt | %s | %s", task.title, db_err)
        spec = _generate_sql_spec(
            llm, task, db_schema, system,
            extra=f"Önceki sorgu şu hatayı verdi, düzelt: {str(db_err)[:400]}\n"
                  f"Önceki sorgu: {spec.sql[:800]}\n")
        try:
            columns, rows = execute_sql(spec.sql)
        except Exception as db_err2:
            raise TaskFailure(f"SQL çalıştırılamadı: {str(db_err2)[:200]}") from db_err2

    if not rows:
        logger.warning("Bos sonuc, genis aralik retry | %s", task.title)
        spec = _generate_sql_spec(
            llm, task, db_schema, system,
            extra="Önceki sorgu hiç satır döndürmedi (muhtemelen tarih filtresi çok dar). "
                  "Tarih filtresini kaldır veya çok daha geniş bir aralık kullan (örn. son 2 yıl).\n")
        try:
            columns, rows = execute_sql(spec.sql)
        except Exception as retry_err:
            raise TaskFailure(f"SQL çalıştırılamadı: {str(retry_err)[:200]}") from retry_err
        if not rows:
            raise TaskFailure("Veritabanında bu görev için veri bulunamadı")

    return spec, columns, rows


def _run_sql_chart(llm: LLMClient, task: PlanTask, db_schema: str) -> dict:
    from tasks.ai_tasks import rows_to_chartjs

    spec, columns, rows = _execute_with_retry(llm, task, db_schema, SQL_GEN_SYSTEM)
    chart_data = rows_to_chartjs(columns, rows, spec.y_label)
    return {
        'type': spec.chart_type or task.chart_type,
        'title': spec.title or task.title,
        'data': chart_data,
    }


def _run_forecast(llm: LLMClient, task: PlanTask, db_schema: str) -> dict:
    """Tarihsel aylik seriyi SQL ile ceker, PyCaret engine /forecast'a gonderir,
    gecmis + tahmini tek cizgi grafiginde birlestirir."""
    engine_url = (os.getenv('PYCARET_ENGINE_URL')
                  or os.getenv('AI_SERVICE_2_URL') or '').rstrip('/')
    if not engine_url:
        raise TaskFailure("PYCARET_ENGINE_URL tanımlı değil")

    spec, columns, rows = _execute_with_retry(llm, task, db_schema, SERIES_SQL_SYSTEM)
    series = []
    for r in rows:
        try:
            series.append({'period': str(r[0]), 'value': float(r[1])})
        except (TypeError, ValueError, IndexError):
            continue
    if len(series) < 4:
        raise TaskFailure(f"Tahmin için yeterli tarihsel veri yok ({len(series)} nokta)")

    try:
        resp = http_requests.post(
            f"{engine_url}/forecast",
            json={'series': series, 'horizon': task.horizon, 'seasonality': 12},
            timeout=30,
        )
    except Exception as e:
        raise TaskFailure(f"Forecast servisi erişilemez: {str(e)[:150]}") from e
    if resp.status_code >= 400:
        raise TaskFailure(f"Forecast servisi HTTP {resp.status_code}: {resp.text[:150]}")

    forecast = resp.json().get('forecast', [])
    if not forecast:
        raise TaskFailure("Forecast servisi boş tahmin döndürdü")

    hist_labels = [p['period'] for p in series]
    fc_labels = [p['period'] for p in forecast]
    labels = hist_labels + fc_labels
    actual = [p['value'] for p in series] + [None] * len(fc_labels)
    # Kesikli tahmin cizgisi gecmisin son noktasindan baslasin
    predicted = [None] * (len(hist_labels) - 1) + [series[-1]['value']] + \
                [round(float(p['value']), 2) for p in forecast]

    return {
        'type': 'line',
        'title': spec.title or task.title,
        'data': {
            'labels': labels,
            'datasets': [
                {'label': 'Gerçekleşen', 'data': actual,
                 'borderColor': 'rgba(59,130,246,1)',
                 'backgroundColor': 'rgba(59,130,246,0.8)', 'borderWidth': 2},
                {'label': 'Tahmin', 'data': predicted,
                 'borderColor': 'rgba(245,158,11,1)',
                 'backgroundColor': 'rgba(245,158,11,0.8)',
                 'borderWidth': 2, 'borderDash': [6, 4]},
            ],
        },
    }


def _summarize_chart(chart: dict) -> str:
    data = chart.get('data', {})
    labels = data.get('labels', [])[:12]
    parts = [f"Grafik: {chart.get('title')} ({chart.get('type')})",
             f"  Etiketler: {', '.join(str(l) for l in labels)}"]
    for ds in data.get('datasets', [])[:3]:
        values = [v for v in ds.get('data', []) if v is not None][:12]
        total = round(sum(v for v in values if isinstance(v, (int, float))), 2)
        parts.append(f"  {ds.get('label', 'Seri')}: {values} (toplam≈{total})")
    return "\n".join(parts)


def _run_narrative(llm: LLMClient, question: str, narrative_task: PlanTask | None,
                   charts: list[dict], failed: list[dict],
                   history: list[dict]) -> str:
    chart_block = "\n\n".join(_summarize_chart(c) for c in charts) if charts \
        else "(Grafik üretilmedi)"
    failed_block = ""
    if failed:
        failed_block = "\nBaşarısız görevler:\n" + "\n".join(
            f"- {f['title']}: {f['reason']}" for f in failed)

    instruction = (narrative_task.question_fragment if narrative_task
                   else "Üretilen grafiklerin kısa bir özetini yap (1-2 cümle).")

    messages = []
    for h in history[-4:]:
        role = 'assistant' if h['role'] in ('assistant', 'model') else 'user'
        messages.append({'role': role, 'content': h['content'][:500]})
    messages.append({'role': 'user', 'content':
        f"Kullanıcı sorusu: {question}\n\n"
        f"Hesaplanan gerçek veriler:\n{chart_block}\n{failed_block}\n\n"
        f"Görev: {instruction}"})

    return llm.generate(NARRATIVE_SYSTEM, messages, temperature=0.0, max_tokens=1024)


# ── PyCaret explicit predict (UI'dan predict_data ile gelen istekler) ────

def _try_pycaret_predict(message: dict, request_id: str, question: str) -> bool:
    engine_url = (os.getenv('PYCARET_ENGINE_URL')
                  or os.getenv('AI_SERVICE_2_URL') or '').rstrip('/')
    company_id = message.get('company_id', '')
    req = message.get('predict_request') or {}
    table_name = req.get('table_name') or message.get('table_name')
    predict_data = req.get('data') or message.get('predict_data')

    if not (engine_url and company_id and table_name and isinstance(predict_data, dict)):
        return False

    try:
        resp = http_requests.post(
            f"{engine_url}/predict",
            json={'company_id': company_id, 'table_name': table_name, 'data': predict_data},
            timeout=20,
        )
        if resp.status_code >= 400:
            logger.warning("PyCaret predict HTTP %s", resp.status_code)
            return False
        body = resp.json()
        post_result(request_id, 'completed', {
            'type': 'predictive', 'success': True, 'question': question,
            'answer': 'PyCaret tahmin sonucu oluşturuldu.',
            'charts': [], 'predictions': body.get('predictions', body),
            'answeredAt': datetime.utcnow().isoformat(),
        })
        return True
    except Exception as e:
        logger.warning("PyCaret predict hatasi: %s", e)
        return False


# ── Ana akis ─────────────────────────────────────────────────────────────

def _parse_history(context) -> list[dict]:
    history = []
    for item in context or []:
        try:
            parsed = json.loads(item) if isinstance(item, str) else item
            role = parsed.get('role', 'user')
            content = parsed.get('content', '')
            if content:
                history.append({'role': 'assistant' if role in ('model', 'assistant') else 'user',
                                'content': content})
        except (json.JSONDecodeError, AttributeError):
            continue
    return history


def process_question(message: dict):
    from llm.metrics import metrics_prompt_block
    from tasks.ai_tasks import DB_SCHEMA as BASE_SCHEMA

    # Sema + metrik sozlugu birlikte tum LLM cagrilarina gider
    DB_SCHEMA = BASE_SCHEMA + metrics_prompt_block()

    request_id = message.get('request_id', '')
    question = message.get('question', '').strip()
    history = _parse_history(message.get('context', []))

    logger.info("Soru alindi | RequestId=%s | Soru=%s", request_id, question)

    # UI'dan hazir predict payload'u geldiyse eski explicit yol
    if _try_pycaret_predict(message, request_id, question):
        return

    llm = LLMClient()
    post_progress(request_id, 5, 'Soru analiz ediliyor…')

    try:
        plan = build_plan(question, history, llm, DB_SCHEMA)
    except LLMError as e:
        logger.error("Planner hatasi | RequestId=%s | %s", request_id, e)
        post_result(request_id, 'error', {
            'type': 'error', 'success': False, 'question': question,
            'answer': f'Soru analiz edilemedi: {str(e)[:200]}',
            'charts': [], 'answeredAt': datetime.utcnow().isoformat(),
        })
        return

    data_tasks = [t for t in plan.tasks if t.kind != 'narrative']
    narrative_task = next((t for t in plan.tasks if t.kind == 'narrative'), None)

    charts: list[dict] = []
    failed: list[dict] = []
    total_steps = len(data_tasks) + (1 if narrative_task or data_tasks else 0)

    for i, task in enumerate(data_tasks):
        pct = 10 + int(75 * i / max(total_steps, 1))
        post_progress(request_id, pct,
                      f"{i + 1}/{total_steps}: {task.title or task.question_fragment[:40]} hazırlanıyor…")
        try:
            if task.kind == 'forecast':
                charts.append(_run_forecast(llm, task, DB_SCHEMA))
            else:
                charts.append(_run_sql_chart(llm, task, DB_SCHEMA))
        except TaskFailure as tf:
            logger.warning("Gorev basarisiz | %s | %s", task.title, tf)
            failed.append({'title': task.title or task.question_fragment[:60],
                           'reason': str(tf)})
        except LLMError as le:
            logger.error("Gorev LLM hatasi | %s | %s", task.title, le)
            failed.append({'title': task.title or task.question_fragment[:60],
                           'reason': f'LLM hatası: {str(le)[:150]}'})

    # Narrative en son — gercek hesaplanmis verilerle
    answer = ''
    if narrative_task or charts or failed:
        post_progress(request_id, 90, 'Özet hazırlanıyor…')
        try:
            answer = _run_narrative(llm, question, narrative_task, charts, failed, history)
        except (LLMError, Exception) as e:
            logger.error("Narrative hatasi | %s", e)
            if charts:
                answer = f'{len(charts)} grafik oluşturuldu.'

    success = bool(charts) or bool(answer.strip())
    result = {
        'type': 'composite',
        'success': success,
        'question': question,
        'answer': answer if success else
            'Hiçbir görev tamamlanamadı. ' + '; '.join(f["reason"] for f in failed[:3]),
        'charts': charts,
        'failedTasks': failed,
        'answeredAt': datetime.utcnow().isoformat(),
    }
    post_result(request_id, 'completed' if success else 'error', result)
