"""Evidence-based report contract. Never infer methodology from LLM prose."""
from datetime import datetime, timezone
import hashlib
import json
import time
import re

VERSION = "analysis-report-v1"


class TracedDataPort:
    def __init__(self, inner, events, started):
        self.inner, self.events, self.started = inner, events, started

    def __getattr__(self, key):
        return getattr(self.inner, key)

    def _call(self, operation, *args, **kwargs):
        began = time.perf_counter()
        event = {"stage": "data_read", "operation": operation,
                 "at": datetime.now(timezone.utc).isoformat(),
                 "elapsedMs": round((began - self.started) * 1000)}
        self.events.append(event)
        try:
            result = getattr(self.inner, operation)(*args, **kwargs)
            event["status"] = "completed"
            return result
        except Exception:
            event["status"] = "failed"
            raise
        finally:
            event["durationMs"] = round((time.perf_counter() - began) * 1000)

    def read_sql(self, *args, **kwargs):
        return self._call("read_sql", *args, **kwargs)

    def scalar(self, *args, **kwargs):
        return self._call("scalar", *args, **kwargs)


def check_metric(config, params):
    """Common rules apply without requiring users to identify database columns."""
    from analysis_policy import GENERAL_POLICY
    table = str(params.get("target_table") or "").replace("[", "").replace("]", "").casefold()
    raw = str(params.get("target_column") or "").replace("[", "").replace("]", "")
    column = raw.rsplit(".", 1)[-1].casefold()
    candidates = [c for c in config.get("columns", []) if str(c.get("column", "")).casefold() == column]
    qualifier = raw.rsplit(".", 1)[0].casefold() if "." in raw else table
    matches = [c for c in candidates if str(c.get("table", "")).casefold() == qualifier
               or str(c.get("table", "")).split(".")[-1].casefold() == qualifier]
    metadata = matches[0] if len(matches) == 1 else candidates[0] if len(candidates) == 1 else None
    kind = str(params.get("analysis_type") or "aggregation").strip().lower()
    if kind not in ("aggregation", "list", "forecast", "statistics", "correlation", "regression", "classification", "anomaly", "clustering"):
        kind = "aggregation"
    aggregation = str(params.get("aggregation") or "count").strip().lower()
    if kind in ("aggregation", "forecast") and aggregation != "count" and any(c.get("role") == "identifier" for c in (matches or candidates)):
        raise ValueError("Kimlik alanı ölçü olarak kullanılamaz. Hangi tutar veya miktarı hesaplamak istediğinizi sorunuzu açıklayarak belirtin.")
    meaning = metadata.get("meaning") if metadata else None
    return {**GENERAL_POLICY, "source": "platform", "label": meaning if meaning and meaning != "unknown" else raw or "Kayıt sayısı",
            "unit": "adet" if aggregation == "count" else "Belirtilmedi", "forecastEnabled": True}


def check_metrics(config, params):
    primary = check_metric(config, params)
    measures = params.get("measures")
    if isinstance(measures, list):
        for index, measure in enumerate(measures):
            if isinstance(measure, dict):
                rule = check_metric(config, {**params, "target_column": measure.get("column"),
                                             "aggregation": measure.get("aggregation")})
                if index == 0:
                    primary = rule
    union = params.get("union")
    branches = union if isinstance(union, list) else union.get("with", []) if isinstance(union, dict) else []
    for branch in branches:
        if isinstance(branch, dict):
            check_metric(config, {**params, "target_table": branch.get("table"),
                                 "target_column": branch.get("target_column", params.get("target_column"))})
    return primary


def build_report(result, audit, config=None):
    kind = audit.get("analysisType", "unknown")
    rule = audit.get("metricRule") or {}
    warnings = [str(i.get("description")) for i in result.get("insights", [])
                if i.get("type") == "warning" and i.get("description")]
    if not rule.get("unit") or rule.get("unit") == "Belirtilmedi":
        warnings.append("Ölçünün birimi doğrulanmadığı için para birimi veya başka bir birim varsayılmadı.")
    methods = {
        "sum": "Seçilen kayıtların toplamı", "count": "Seçilen kayıtların sayısı",
        "avg": "Seçilen kayıtların ortalaması", "min": "En küçük değer",
        "max": "En büyük değer",
    }
    if kind == "forecast":
        method = f"{audit.get('forecastModel', 'Belirtilmedi')}; {audit.get('periodCount', 0)} aylık geçmişten {audit.get('horizon', 0)} ay ileri tahmin."
        warnings.extend([
            "Geçmiş desenlerin devam edeceği varsayılır; dış etkenler bu modele dahil değildir.",
            "Geçmiş dönemlerde tahmin başarısı henüz ölçülmedi; sonuç keşif amaçlıdır.",
            "Alt ve üst sınırlar yaklaşık belirsizlik bantlarıdır; %95 kapsama doğrulanmamıştır.",
        ])
    elif kind == "aggregation":
        method = methods.get(audit.get("aggregation"), str(audit.get("aggregation", "Belirtilmedi")))
        if audit.get("window"):
            method += f"; pencere işlemi: {audit['window']}"
        if audit.get("periodCompare"):
            method += "; dönem karşılaştırması uygulanmıştır"
    else:
        method = {"list": "Filtrelenmiş kayıtların sıralı listesi", "statistics": "Betimsel istatistikler",
                  "correlation": "Değişkenler arasındaki istatistiksel ilişki",
                  "regression": "PyCaret ile karşılaştırmalı regresyon",
                  "classification": "PyCaret ile karşılaştırmalı sınıflandırma",
                  "anomaly": "Sapma analizi", "clustering": "PyCaret ile kümeleme"}.get(kind, "Yöntem bilgisi mevcut değil")
    if kind in ("regression", "correlation", "classification"):
        warnings.append("İstatistiksel ilişki veya değişken önemi nedensellik kanıtı değildir.")
    if audit.get("truncated"):
        warnings.append("Okuma sınırına ulaşıldı; sonuç verinin tamamını temsil etmeyebilir.")
    if audit.get("filledGaps"):
        warnings.append(f"{audit['filledGaps']} boş dönem, kayıtlı eksik veri kuralına göre sıfır kabul edildi.")
    tables = []
    for chart in result.get("charts", []):
        data = chart.get("data") or {}
        if isinstance(data.get("rows"), list):
            tables.append({"title": chart.get("title"), "columns": data.get("columns", []), "rows": data["rows"]})
        elif chart.get("type") == "scatter":
            rows = [[d.get("label"), p.get("x"), p.get("y")] for d in data.get("datasets", []) for p in d.get("data", [])]
            tables.append({"title": chart.get("title"), "columns": ["Seri", "X", "Y"], "rows": rows})
        elif isinstance(data.get("labels"), list):
            datasets = data.get("datasets") or []
            rows = [[label] + [d.get("data", [])[i] if i < len(d.get("data", [])) else None
                              for d in datasets] for i, label in enumerate(data["labels"])]
            # The chart connects forecast to the final observation; that bridge is not a prediction.
            history = audit.get("periodCount", 0)
            if kind == "forecast" and history and len(rows) >= history and len(datasets) >= 2:
                rows[history - 1][2] = None
            tables.append({"title": chart.get("title"), "columns": ["Dönem / kategori"] +
                           [d.get("label") or "Değer" for d in datasets], "rows": rows})
    for table in tables:
        explanations = []
        units = []
        for column in table["columns"]:
            aggregate = re.fullmatch(r"(SUM|AVG|MIN|MAX|COUNT)\(([^)]+)\)", column, re.IGNORECASE)
            unit = None
            if column == "Dönem / kategori":
                unit = None
            elif column.startswith("Adet") or aggregate and aggregate.group(1).upper() == "COUNT":
                unit = "adet"
            elif kind == "forecast" and audit.get("aggregation") == "count":
                unit = "adet"
            units.append(unit)
            description = {
                "Dönem / kategori": "Hesaplamanın gruplandığı dönem veya kategori.",
                "Gerçekleşen": "Geçmiş veriden hesaplanan gözlenen değer.",
                "Tahmin": "Gelecek dönem için modelin ürettiği öngörü.",
                "Alt sınır (yaklaşık)": "Yaklaşık belirsizlik bandının alt sınırı.",
                "Üst sınır (yaklaşık)": "Yaklaşık belirsizlik bandının üst sınırı.",
            }.get(column)
            if not description:
                metadata = next((c for c in (config or {}).get("columns", [])
                                 if c.get("table") == audit.get("targetTable") and c.get("column") == column), None)
                description = metadata.get("meaning") if metadata else None
            explanations.append(description if description and description != "unknown" else
                                f"{column}: seçilen yöntemle üretilen değer; alanın iş anlamı doğrulanmamış olabilir.")
            if unit:
                explanations[-1] += f" Birim: {unit}."
        table["columnExplanations"] = explanations
        table["columnUnits"] = units
    period = audit.get("dataPeriod")
    if not period and audit.get("timeAxis"):
        labels = [label for chart in result.get("charts", []) for label in (chart.get("data") or {}).get("labels", [])
                  if isinstance(label, str) and re.fullmatch(r"\d{4}(?:-\d{2})?(?:-\d{2})?", label)]
        if labels:
            period = {"from": min(labels), "to": max(labels)}
    report = {
        "version": VERSION, "generatedAt": datetime.now(timezone.utc).isoformat(),
        "metric": rule.get("label") or audit.get("resolvedTargetColumn") or audit.get("targetColumn") or "Kayıt sayısı",
        "unit": rule.get("unit") or "Belirtilmedi", "source": audit.get("targetTable"),
        "joins": audit.get("joins") or [],
        "filters": audit.get("appliedFilters") or [], "timeAxis": audit.get("timeAxis"),
        "dataPeriod": period, "periodScope": "source_series" if audit.get("dataPeriod") else "displayed_result", "method": method,
        "interpretation": "Satırlar dönem veya kategorileri, sütunlar hesaplanan ölçüleri gösterir. Boş hücreler sıfır değildir."
            if kind != "forecast" else "Gerçekleşen değerler geçmişi, tahmin sütunu geleceğe yönelik öngörüyü gösterir. Tahmin kesin sonuç değildir.",
        "warnings": list(dict.fromkeys(warnings)), "tables": tables,
        "notices": [audit["horizonNotice"]] if audit.get("horizonNotice") else [],
        "policyVersion": rule.get("version"),
        "quality": "exploratory" if kind == "forecast" else "warning" if warnings else "rules_applied",
    }
    if audit.get("aggregation") == "count" and kind in ("aggregation", "forecast"):
        report["unit"] = "adet"
    if audit.get("measures"):
        report["metric"] = " · ".join(audit["measures"])
        report["unit"] = "Sütun açıklamalarında"
        report["method"] = "Birden fazla ölçü SQL'de hesaplandı: " + " · ".join(audit["measures"])
    evidence = {"charts": result.get("charts", []), "report": report}
    report["snapshotHash"] = hashlib.sha256(json.dumps(evidence, ensure_ascii=False, sort_keys=True,
                                                      default=str).encode()).hexdigest()
    return report
