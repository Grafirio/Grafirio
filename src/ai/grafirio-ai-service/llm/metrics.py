"""
Metrik sozlugu (semantic layer) yukleyicisi.

metrics.yaml'daki is metrigi tanimlarini okur ve LLM prompt'una eklenecek
metin blogunu uretir. Amac: "karlilik" gibi metriklerin formulunun her
soruda ayni olmasi — LLM formul icat etmez, secer.
"""
import functools
import logging
import os

logger = logging.getLogger(__name__)

_METRICS_PATH = os.path.join(
    os.path.dirname(os.path.dirname(os.path.abspath(__file__))), 'metrics.yaml')


@functools.lru_cache(maxsize=1)
def load_metrics() -> list[dict]:
    try:
        import yaml
        with open(_METRICS_PATH, encoding='utf-8') as f:
            data = yaml.safe_load(f)
        return data if isinstance(data, list) else []
    except FileNotFoundError:
        logger.warning("metrics.yaml bulunamadi: %s", _METRICS_PATH)
        return []
    except Exception as e:
        logger.error("metrics.yaml okunamadi: %s", e)
        return []


@functools.lru_cache(maxsize=1)
def metrics_prompt_block() -> str:
    metrics = load_metrics()
    if not metrics:
        return ''
    lines = [
        "\nMetrik sözlüğü (soru bu metriklerden biriyle eşleşiyorsa "
        "sql_expression ve required_joins değerlerini AYNEN kullan, formül icat etme):",
    ]
    for m in metrics:
        aliases = ', '.join(m.get('aliases', []))
        lines.append(
            f"- {m.get('name')} ({aliases}): {m.get('description', '')}\n"
            f"    SQL: {m.get('sql_expression')}\n"
            f"    FROM/JOIN: {m.get('required_joins')} | grain: {m.get('grain', 'monthly')}")
    return "\n".join(lines) + "\n"
