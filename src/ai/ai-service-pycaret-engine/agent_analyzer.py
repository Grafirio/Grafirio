"""
PyCaret Agent Analyzer — LLM config'i ile dinamik analiz yapan modül.
MassTransit'ten gelen mesajları işler ve sonuçları geri gönderir.
"""

import json
import logging
import pandas as pd
import sqlalchemy
from typing import Dict, Any, List, Optional

logger = logging.getLogger(__name__)


class AgentAnalyzer:
    """LLM tarafından oluşturulan config ve parametrelerle veri analizi yapar."""

    def __init__(self, connection_string: str):
        self.engine = sqlalchemy.create_engine(connection_string)

    def run_analysis(self, config: Dict, params: Dict) -> Dict[str, Any]:
        """
        Config ve parametrelere göre analiz yap, grafik verileri ve insights döndür.
        """
        analysis_type = params.get("analysis_type", "statistics")
        target_table = params.get("target_table")
        target_column = params.get("target_column")
        feature_columns = params.get("feature_columns", [])
        group_by = params.get("group_by", [])
        aggregation = params.get("aggregation", "count")
        sort_by = params.get("sort_by")
        sort_order = params.get("sort_order", "desc")
        limit = params.get("limit", 10)
        chart_type = params.get("chart_type", "bar")
        chart_title = params.get("chart_title", "Analiz Sonucu")
        description = params.get("description", "")
        filters = params.get("filters", {})

        if not target_table:
            # Config'den ilk tabloyu al
            tables = config.get("tables", [])
            if tables:
                target_table = tables[0].get("name", "")

        if not target_table:
            return self._error_result("Hedef tablo belirlenemedi")

        try:
            # Tablodan veri çek
            df = self._load_table_data(target_table, filters, limit * 10)

            if df.empty:
                return self._error_result(f"'{target_table}' tablosunda veri bulunamadı")

            # Analiz tipine göre işlem yap
            if analysis_type in ("statistics", "correlation"):
                return self._statistics_analysis(df, target_column, chart_type, chart_title, description)
            elif analysis_type == "regression":
                return self._regression_analysis(df, target_column, feature_columns, chart_type, chart_title, description)
            elif analysis_type == "classification":
                return self._classification_analysis(df, target_column, feature_columns, chart_type, chart_title, description)
            elif analysis_type == "anomaly":
                return self._anomaly_analysis(df, feature_columns, chart_type, chart_title, description)
            elif analysis_type == "clustering":
                return self._clustering_analysis(df, feature_columns, chart_type, chart_title, description)
            else:
                # Genel gruplama / aggregation
                return self._aggregation_analysis(
                    df, target_column, group_by, aggregation,
                    sort_by, sort_order, limit, chart_type, chart_title, description
                )

        except Exception as e:
            logger.error(f"Analiz hatası: {e}")
            return self._error_result(str(e))

    def _load_table_data(self, table_name: str, filters: Dict, max_rows: int = 5000) -> pd.DataFrame:
        """Tablodan veri yükle."""
        # Schema prefix varsa ayır
        parts = table_name.split(".")
        schema = parts[0] if len(parts) > 1 else "dbo"
        table = parts[-1]

        query = f"SELECT TOP {max_rows} * FROM [{schema}].[{table}]"

        if filters:
            conditions = []
            for col, val in filters.items():
                conditions.append(f"[{col}] = '{val}'")
            if conditions:
                query += " WHERE " + " AND ".join(conditions)

        logger.info(f"SQL: {query}")
        return pd.read_sql(query, self.engine)

    def _statistics_analysis(self, df: pd.DataFrame, target_col: Optional[str],
                             chart_type: str, title: str, desc: str) -> Dict:
        """Temel istatistik analizi."""
        numeric_df = df.select_dtypes(include=["number"])

        if numeric_df.empty:
            return self._error_result("Sayısal kolon bulunamadı")

        stats = numeric_df.describe().to_dict()

        # Grafik: her kolonun ortalamasını göster
        cols = list(numeric_df.columns)[:15]
        means = [float(numeric_df[c].mean()) for c in cols]

        charts = [{
            "type": chart_type,
            "title": title or "Kolon Ortalamaları",
            "data": {
                "labels": cols,
                "datasets": [{"label": "Ortalama", "data": means}]
            }
        }]

        # Korelasyon grafiği
        if len(cols) >= 2:
            corr = numeric_df[cols].corr()
            top_corr_pairs = []
            for i in range(len(cols)):
                for j in range(i + 1, len(cols)):
                    top_corr_pairs.append({
                        "pair": f"{cols[i]} ↔ {cols[j]}",
                        "value": round(float(corr.iloc[i, j]), 3)
                    })
            top_corr_pairs.sort(key=lambda x: abs(x["value"]), reverse=True)
            top5 = top_corr_pairs[:8]

            charts.append({
                "type": "bar",
                "title": "En Yüksek Korelasyonlar",
                "data": {
                    "labels": [p["pair"] for p in top5],
                    "datasets": [{"label": "Korelasyon", "data": [p["value"] for p in top5]}]
                }
            })

        insights = [
            {"type": "info", "title": "Toplam Satır", "description": f"{len(df)} satır analiz edildi"},
            {"type": "info", "title": "Sayısal Kolon", "description": f"{len(cols)} sayısal kolon bulundu"},
        ]

        if target_col and target_col in numeric_df.columns:
            insights.append({
                "type": "success",
                "title": f"{target_col} İstatistikleri",
                "description": f"Ort: {numeric_df[target_col].mean():.2f}, "
                               f"Min: {numeric_df[target_col].min():.2f}, "
                               f"Max: {numeric_df[target_col].max():.2f}"
            })

        return {
            "success": True,
            "charts": charts,
            "insights": insights,
            "summary": desc or f"{len(df)} satır üzerinde istatistik analizi tamamlandı."
        }

    def _regression_analysis(self, df: pd.DataFrame, target_col: Optional[str],
                             features: List[str], chart_type: str, title: str, desc: str) -> Dict:
        """PyCaret regression analizi."""
        numeric_df = df.select_dtypes(include=["number"]).dropna()

        if target_col not in numeric_df.columns:
            return self._statistics_analysis(df, target_col, chart_type, title, desc)

        if len(numeric_df) < 30:
            return self._error_result("Regression için yeterli veri yok (min 30 satır)")

        try:
            from pycaret.regression import setup, compare_models, pull

            feature_cols = [c for c in features if c in numeric_df.columns and c != target_col]
            if not feature_cols:
                feature_cols = [c for c in numeric_df.columns if c != target_col][:10]

            use_df = numeric_df[feature_cols + [target_col]].copy()

            setup(data=use_df, target=target_col, session_id=42,
                  verbose=False, html=False, n_jobs=1)
            best = compare_models(n_select=1, sort="MAE", verbose=False)
            results_df = pull()

            model_names = results_df.index.tolist()[:5]
            mae_values = results_df["MAE"].head(5).tolist()
            r2_values = results_df["R2"].head(5).tolist()

            charts = [
                {
                    "type": "bar",
                    "title": title or f"{target_col} Tahmin Modeli Karşılaştırma (MAE)",
                    "data": {
                        "labels": model_names,
                        "datasets": [{"label": "MAE (Düşük = İyi)", "data": [round(v, 4) for v in mae_values]}]
                    }
                },
                {
                    "type": "bar",
                    "title": f"{target_col} Model R² Skorları",
                    "data": {
                        "labels": model_names,
                        "datasets": [{"label": "R² (Yüksek = İyi)", "data": [round(v, 4) for v in r2_values]}]
                    }
                }
            ]

            insights = [
                {"type": "success", "title": "En İyi Model", "description": f"{type(best).__name__}"},
                {"type": "info", "title": "Kullanılan Özellikler", "description": ", ".join(feature_cols[:5])},
                {"type": "info", "title": "Veri Boyutu", "description": f"{len(use_df)} satır, {len(feature_cols)} özellik"},
            ]

            return {"success": True, "charts": charts, "insights": insights,
                    "summary": desc or f"{target_col} için regression analizi tamamlandı. En iyi model: {type(best).__name__}"}

        except Exception as e:
            logger.error(f"Regression hatası: {e}")
            return self._statistics_analysis(df, target_col, chart_type, title, desc)

    def _classification_analysis(self, df: pd.DataFrame, target_col: Optional[str],
                                 features: List[str], chart_type: str, title: str, desc: str) -> Dict:
        """PyCaret classification analizi."""
        if not target_col or target_col not in df.columns:
            return self._statistics_analysis(df, target_col, chart_type, title, desc)

        numeric_df = df.select_dtypes(include=["number"]).copy()
        if target_col not in numeric_df.columns:
            numeric_df[target_col] = df[target_col]

        numeric_df = numeric_df.dropna(subset=[target_col])

        if len(numeric_df) < 30:
            return self._error_result("Classification için yeterli veri yok")

        try:
            from pycaret.classification import setup, compare_models, pull

            feature_cols = [c for c in features if c in numeric_df.columns and c != target_col]
            if not feature_cols:
                feature_cols = [c for c in numeric_df.select_dtypes(include=["number"]).columns if c != target_col][:10]

            use_df = numeric_df[feature_cols + [target_col]].copy()

            setup(data=use_df, target=target_col, session_id=42,
                  verbose=False, html=False, n_jobs=1)
            best = compare_models(n_select=1, sort="Accuracy", verbose=False)
            results_df = pull()

            model_names = results_df.index.tolist()[:5]
            acc_values = results_df["Accuracy"].head(5).tolist()

            charts = [{
                "type": "bar",
                "title": title or f"{target_col} Classification Model Doğruluğu",
                "data": {
                    "labels": model_names,
                    "datasets": [{"label": "Accuracy", "data": [round(v, 4) for v in acc_values]}]
                }
            }]

            # Sınıf dağılımı
            value_counts = df[target_col].value_counts().head(10)
            charts.append({
                "type": "pie",
                "title": f"{target_col} Sınıf Dağılımı",
                "data": {
                    "labels": [str(x) for x in value_counts.index.tolist()],
                    "datasets": [{"label": "Adet", "data": value_counts.values.tolist()}]
                }
            })

            insights = [
                {"type": "success", "title": "En İyi Model", "description": type(best).__name__},
                {"type": "info", "title": "Sınıf Sayısı", "description": str(df[target_col].nunique())},
            ]

            return {"success": True, "charts": charts, "insights": insights,
                    "summary": desc or f"{target_col} classification analizi tamamlandı."}

        except Exception as e:
            logger.error(f"Classification hatası: {e}")
            return self._statistics_analysis(df, target_col, chart_type, title, desc)

    def _anomaly_analysis(self, df: pd.DataFrame, features: List[str],
                          chart_type: str, title: str, desc: str) -> Dict:
        """Anomaly detection analizi."""
        numeric_df = df.select_dtypes(include=["number"]).dropna()

        if len(numeric_df) < 30:
            return self._error_result("Anomaly detection için yeterli veri yok")

        try:
            from pycaret.anomaly import setup, create_model, assign_model

            cols = [c for c in features if c in numeric_df.columns][:10]
            if not cols:
                cols = numeric_df.columns.tolist()[:10]

            use_df = numeric_df[cols].copy()

            setup(data=use_df, session_id=42, verbose=False, html=False)
            model = create_model("iforest")
            results = assign_model(model)

            anomaly_count = int(results["Anomaly"].sum())
            normal_count = len(results) - anomaly_count

            charts = [{
                "type": "doughnut",
                "title": title or "Anomali Dağılımı",
                "data": {
                    "labels": ["Normal", "Anomali"],
                    "datasets": [{"label": "Adet", "data": [normal_count, anomaly_count]}]
                }
            }]

            # Anomaly score dağılımı
            scores = results["Anomaly_Score"].values
            import numpy as np
            hist, edges = np.histogram(scores, bins=10)
            charts.append({
                "type": "bar",
                "title": "Anomali Skor Dağılımı",
                "data": {
                    "labels": [f"{edges[i]:.2f}" for i in range(len(hist))],
                    "datasets": [{"label": "Frekans", "data": hist.tolist()}]
                }
            })

            insights = [
                {"type": "warning" if anomaly_count > 0 else "success",
                 "title": "Anomali Tespit",
                 "description": f"{anomaly_count} anomali tespit edildi ({anomaly_count / len(results) * 100:.1f}%)"},
                {"type": "info", "title": "Analiz Edilen Veri",
                 "description": f"{len(results)} satır, {len(cols)} özellik"},
            ]

            return {"success": True, "charts": charts, "insights": insights,
                    "summary": desc or f"Anomaly detection tamamlandı. {anomaly_count} anomali bulundu."}

        except Exception as e:
            logger.error(f"Anomaly hatası: {e}")
            return self._error_result(str(e))

    def _clustering_analysis(self, df: pd.DataFrame, features: List[str],
                             chart_type: str, title: str, desc: str) -> Dict:
        """Clustering analizi."""
        numeric_df = df.select_dtypes(include=["number"]).dropna()

        if len(numeric_df) < 30:
            return self._error_result("Clustering için yeterli veri yok")

        try:
            from pycaret.clustering import setup, create_model, assign_model

            cols = [c for c in features if c in numeric_df.columns][:10]
            if not cols:
                cols = numeric_df.columns.tolist()[:10]

            use_df = numeric_df[cols].copy()

            setup(data=use_df, session_id=42, verbose=False, html=False)
            model = create_model("kmeans")
            results = assign_model(model)

            cluster_counts = results["Cluster"].value_counts().sort_index()

            charts = [{
                "type": "pie",
                "title": title or "Kümeleme Dağılımı",
                "data": {
                    "labels": [f"Küme {c}" for c in cluster_counts.index.tolist()],
                    "datasets": [{"label": "Adet", "data": cluster_counts.values.tolist()}]
                }
            }]

            # Küme merkezleri
            if len(cols) >= 2:
                charts.append({
                    "type": "bar",
                    "title": "Küme Ortalamaları (İlk 2 Özellik)",
                    "data": {
                        "labels": [f"Küme {c}" for c in cluster_counts.index.tolist()],
                        "datasets": [
                            {"label": cols[0], "data": [round(float(results[results["Cluster"] == c][cols[0]].mean()), 2) for c in cluster_counts.index]},
                            {"label": cols[1], "data": [round(float(results[results["Cluster"] == c][cols[1]].mean()), 2) for c in cluster_counts.index]},
                        ]
                    }
                })

            insights = [
                {"type": "success", "title": "Küme Sayısı", "description": f"{len(cluster_counts)} küme oluşturuldu"},
                {"type": "info", "title": "Kullanılan Özellikler", "description": ", ".join(cols[:5])},
            ]

            return {"success": True, "charts": charts, "insights": insights,
                    "summary": desc or f"Clustering tamamlandı. {len(cluster_counts)} küme bulundu."}

        except Exception as e:
            logger.error(f"Clustering hatası: {e}")
            return self._error_result(str(e))

    def _aggregation_analysis(self, df: pd.DataFrame, target_col: Optional[str],
                              group_by: List[str], aggregation: str,
                              sort_by: Optional[str], sort_order: str,
                              limit: int, chart_type: str, title: str, desc: str) -> Dict:
        """Gruplama ve aggregation analizi."""
        if not group_by:
            # Kategorik kolonları bul
            cat_cols = df.select_dtypes(include=["object", "category"]).columns.tolist()
            if cat_cols:
                group_by = [cat_cols[0]]
            else:
                return self._statistics_analysis(df, target_col, chart_type, title, desc)

        valid_group = [c for c in group_by if c in df.columns]
        if not valid_group:
            return self._statistics_analysis(df, target_col, chart_type, title, desc)

        try:
            if target_col and target_col in df.columns:
                agg_func = {"count": "count", "sum": "sum", "avg": "mean", "min": "min", "max": "max"}.get(aggregation, "count")
                grouped = df.groupby(valid_group)[target_col].agg(agg_func).reset_index()
                grouped.columns = valid_group + ["value"]
            else:
                grouped = df.groupby(valid_group).size().reset_index(name="value")

            # Sıralama
            ascending = sort_order != "desc"
            grouped = grouped.sort_values("value", ascending=ascending).head(limit)

            labels = grouped[valid_group[0]].astype(str).tolist()
            values = grouped["value"].tolist()

            charts = [{
                "type": chart_type,
                "title": title or f"{valid_group[0]} bazında {aggregation}",
                "data": {
                    "labels": labels,
                    "datasets": [{"label": f"{aggregation.upper()}", "data": [round(float(v), 2) for v in values]}]
                }
            }]

            insights = [
                {"type": "info", "title": "Grup Sayısı", "description": f"{len(grouped)} grup gösteriliyor (toplam {df[valid_group[0]].nunique()})"},
                {"type": "success", "title": "En Yüksek", "description": f"{labels[0] if labels else '-'}: {values[0] if values else 0}"},
            ]

            return {"success": True, "charts": charts, "insights": insights,
                    "summary": desc or f"{valid_group[0]} bazında {aggregation} analizi tamamlandı."}

        except Exception as e:
            logger.error(f"Aggregation hatası: {e}")
            return self._error_result(str(e))

    @staticmethod
    def _error_result(msg: str) -> Dict:
        return {
            "success": False,
            "charts": [],
            "insights": [{"type": "error", "title": "Hata", "description": msg}],
            "summary": f"Analiz başarısız: {msg}"
        }
