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
    """Rules come exclusively from the persisted dictionary, never the question."""
    table = str(params.get("target_table") or "").replace("[", "").replace("]", "").casefold()
    if not table and len(config.get("tables", [])) == 1:
        entry = config["tables"][0]
        table = str(entry.get("name") if isinstance(entry, dict) else entry).casefold()
    if table and "." not in table:
        matches = [str(t.get("name") if isinstance(t, dict) else t) for t in config.get("tables", [])
                   if str(t.get("name") if isinstance(t, dict) else t).split(".")[-1].casefold() == table]
        if len(matches) == 1:
            table = matches[0].casefold()
    raw_column = str(params.get("target_column") or "").replace("[", "").replace("]", "")
    if "." in raw_column:
        qualifier, raw_column = raw_column.rsplit(".", 1)
        candidates = [str(t.get("name") if isinstance(t, dict) else t) for t in config.get("tables", [])]
        matches = [t for t in candidates if t.casefold() == qualifier.casefold() or t.split(".")[-1].casefold() == qualifier.casefold()]
        if len(matches) == 1:
            table = matches[0].casefold()
        else:
            # Unknown aliases must not bypass rules for the underlying metric.
            matches = [c for c in config.get("columns", []) if str(c.get("column", "")).casefold() == raw_column.casefold()]
            if len(matches) == 1:
                table = str(matches[0]["table"]).casefold()
            elif any(str(r.get("column", "")).casefold() == raw_column.casefold() for r in config.get("metricRules", [])):
                raise ValueError("Metrik kuralını uygulamak için alanı gerçek tablo adıyla belirtin.")
    column = raw_column.casefold()
    rules = config.get("metricRules", [])
    rule = next((r for r in rules if str(r.get("table", "")).casefold() == table
                 and str(r.get("column", "")).casefold() == column), None)
    role = next((c.get("role") for c in config.get("columns", [])
                 if str(c.get("table", "")).casefold() == table
                 and str(c.get("column", "")).casefold() == column), None)
    kind = str(params.get("analysis_type") or "aggregation").strip().lower()
    if kind not in ("aggregation", "list", "forecast", "statistics", "correlation", "regression", "classification", "anomaly", "clustering"):
        kind = "aggregation"
    aggregation = str(params.get("aggregation") or "count").strip().lower()
    if kind == "forecast" and rule is None and not params.get("measures"):
        raise ValueError("Tahmin için önce Metrik kuralları ekranından birim, tarih alanı ve tahmin sınırlarını tanımlayın.")
    if role == "identifier" and aggregation != "count" and kind in ("aggregation", "forecast"):
        raise ValueError("Kimlik alanı ölçü olarak kullanılamaz. Bir tutar veya miktar alanı seçin.")
    if rule and kind in ("aggregation", "forecast", "anomaly"):
        if aggregation not in rule["allowedAggregations"]:
            raise ValueError(f"Bu metrik için '{aggregation}' işlemi izinli değil. Metrik kurallarını kontrol edin.")
        axis = params.get("time_axis") or {}
        date = str(axis.get("column", "")).replace("[", "").replace("]", "")
        if rule.get("dateColumn") and axis and (date.split(".")[-1].casefold() != rule["dateColumn"].casefold()
                or "." in date and date.rsplit(".", 1)[0].casefold() not in (table, table.split(".")[-1])):
            raise ValueError(f"Bu metriğin zaman alanı {rule['dateColumn']} olmalı.")
        if kind == "forecast" and not rule.get("forecastEnabled", False):
            raise ValueError("Bu metrik için tahmin kapalı. Önce metrik kurallarından uygunluğunu doğrulayın.")
    return rule


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
    if str(params.get("analysis_type") or "").lower() == "forecast" and primary is None:
        raise ValueError("Tahmin için seçilen metriğin kurallarını önce tanımlayın.")
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
    if not rule:
        warnings.append("Bu ölçünün birimi ve iş kuralları henüz doğrulanmadı.")
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
            source_column = aggregate.group(2) if aggregate else audit.get("resolvedTargetColumn") if len(table["columns"]) == 2 else None
            column_rule = next((r for r in (config or {}).get("metricRules", []) if r.get("table") == audit.get("targetTable")
                                and r.get("column") == source_column), None)
            unit = column_rule.get("unit") if column_rule else None
            if column == "Dönem / kategori":
                unit = None
            elif column.startswith("Adet") or aggregate and aggregate.group(1).upper() == "COUNT":
                unit = "adet"
            elif kind == "forecast":
                unit = "adet" if audit.get("aggregation") == "count" else rule.get("unit")
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
