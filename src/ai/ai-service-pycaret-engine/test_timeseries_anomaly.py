"""Time-series anomaly detection: detector ladder and analyzer wiring."""

import importlib
import sys
import types
import unittest
from unittest.mock import patch

from anomaly_detector import AnomalyError, detect_anomalies

# Keep the production environment dependency-free without leaking a pandas stub.
with patch.dict(sys.modules, {
    "pandas": types.SimpleNamespace(DataFrame=object, isna=lambda value: value is None),
}):
    AgentAnalyzer = importlib.import_module("agent_analyzer").AgentAnalyzer

from test_forecast import TimePort  # noqa: E402
from test_sql_aggregation import ResultFrame  # noqa: E402


class DetectorTests(unittest.TestCase):
    @staticmethod
    def series(values, start_year=2025):
        return [{"period": f"{start_year + i // 12:04d}-{i % 12 + 1:02d}",
                 "value": value} for i, value in enumerate(values)]

    def test_middle_spike_is_flagged_strongest(self):
        values = [10.0] * 4 + [100.0] + [10.0] * 4
        outcome = detect_anomalies(self.series(values))
        self.assertGreaterEqual(len(outcome["anomalies"]), 1)
        top = max(outcome["anomalies"], key=lambda a: abs(a["score"]))
        self.assertEqual(top["period"], "2025-05")
        self.assertGreater(top["score"], 2.5)
        self.assertEqual(len(outcome["expected"]), len(values))

    def test_constant_series_has_no_anomalies(self):
        outcome = detect_anomalies(self.series([42.0] * 12))
        self.assertEqual(outcome["anomalies"], [])
        self.assertEqual(outcome["sigma"], 0)

    def test_requires_minimum_history_and_sorts_input(self):
        with self.assertRaises(AnomalyError):
            detect_anomalies(self.series([1.0] * 7))
        shuffled = list(reversed(self.series([10.0] * 8)))
        outcome = detect_anomalies(shuffled)
        self.assertEqual([e["period"] for e in outcome["expected"][:2]],
                         ["2025-01", "2025-02"])


class QueuePort(TimePort):
    """Returns a different rowset per query — explain needs three reads."""

    def __init__(self, batches):
        super().__init__([])
        self.batches = [list(rows) for rows in batches]

    def read_sql(self, sql, params=None, max_rows=None):
        self.queries.append((sql, dict(params or {})))
        rows = self.batches.pop(0) if self.batches else []
        frame = ResultFrame(rows)
        # The legacy row path records df.columns in the audit trail.
        frame.columns = list(rows[0]) if rows else []
        return frame


class FakeDetectorModule(types.ModuleType):
    """Deterministic stand-in: flags the given periods regardless of library."""

    def __init__(self, flagged):
        super().__init__("anomaly_detector")
        self.AnomalyError = AnomalyError
        self.DEFAULT_THRESHOLD = 2.5
        self.flagged = flagged
        self.calls = []

    def detect_anomalies(self, series, seasonality=12, threshold=2.5):
        self.calls.append({"series": [dict(p) for p in series],
                           "seasonality": seasonality})
        if len(series) < 8:
            raise AnomalyError("Sapma analizi için en az 8 dönem gerekir")
        return {
            "model_name": "FakeLadder",
            "sigma": 1.0,
            "expected": [{"period": p["period"], "value": 10.0} for p in series],
            "anomalies": [
                {"period": p["period"], "actual": p["value"],
                 "expected": 10.0, "score": 4.0}
                for p in series if p["period"] in self.flagged
            ],
        }


class TimeseriesAnomalyTests(unittest.TestCase):
    MONTHS = [f"2025-{m:02d}" for m in range(1, 10)]

    def series_rows(self, spike="2025-05"):
        return [{"Donem": period, "value": 100 if period == spike else 10}
                for period in self.MONTHS]

    def analyze(self, batches, flagged=("2025-05",), **overrides):
        port = QueuePort(batches)
        analyzer = AgentAnalyzer(port)
        params = {"target_table": "dbo.Calculations", "analysis_type": "anomaly",
                  "aggregation": "sum", "target_column": "Amount",
                  "group_by": [], "chart_title": "",
                  "time_axis": {"column": "CreatedDate", "granularity": "month"}}
        params.update(overrides)
        config = {
            "tables": [{"name": f"dbo.{table}"} for table in port.TABLES],
            "columns": [{"table": f"dbo.{table}", "column": column}
                        for table, columns in port.TABLES.items() for column in columns],
            "relationships": [],
        }
        fake = FakeDetectorModule(set(flagged))
        with patch.dict(sys.modules, {"anomaly_detector": fake}):
            return analyzer.run_analysis(config, params), port, fake

    def test_series_anomaly_charts_actual_expected_and_flags(self):
        result, port, fake = self.analyze([self.series_rows()])
        self.assertTrue(result["success"], result)

        sql = port.queries[0][0]
        bucket = "CONVERT(char(7), [t0].[CreatedDate], 126)"
        self.assertIn(f"GROUP BY {bucket}", sql)
        self.assertIn(f"ORDER BY {bucket} ASC", sql)
        self.assertEqual(fake.calls[0]["seasonality"], 12)

        chart = result["charts"][0]
        self.assertEqual(chart["type"], "line")
        data = chart["data"]
        self.assertEqual([d["label"] for d in data["datasets"]],
                         ["Gerçekleşen", "Beklenen", "Sapma"])
        flags = data["datasets"][2]["data"]
        self.assertEqual(flags[4], 100)
        self.assertEqual([v for v in flags if v is not None], [100])

        text = str(result["insights"])
        self.assertIn("2025-05", text)
        self.assertIn("σ", text)
        self.assertEqual(result["audit"]["anomalies"], ["2025-05"])
        self.assertEqual(result["audit"]["anomalyModel"], "FakeLadder")

    def test_quiet_series_reports_no_anomaly(self):
        result, _, _ = self.analyze([self.series_rows()], flagged=())
        self.assertTrue(result["success"], result)
        self.assertIn("Sapma yok", str(result["insights"]))
        self.assertEqual(result["audit"]["anomalies"], [])

    def test_explain_compares_anomalous_period_with_previous(self):
        current = [{"Country": "DE", "value": 80}, {"Country": "NL", "value": 20}]
        previous = [{"Country": "DE", "value": 10}, {"Country": "NL", "value": 15}]
        result, port, _ = self.analyze(
            [self.series_rows(), current, previous], explain_by="Country")
        self.assertTrue(result["success"], result)

        self.assertEqual(len(port.queries), 3)
        bucket = "CONVERT(char(7), [t0].[CreatedDate], 126)"
        for position, (key, period) in enumerate(
                [("exp0", "2025-05"), ("exp1", "2025-04")], start=1):
            sql, bound = port.queries[position]
            self.assertIn(f"{bucket} = :{key}", sql)
            self.assertIn("GROUP BY [t0].[Country]", sql)
            self.assertIn(f"TOP ({AgentAnalyzer.EXPLAIN_LIMIT})", sql)
            self.assertEqual(bound[key], period)

        text = str(result["insights"])
        self.assertIn("Sapmayı sürükleyenler (2025-05)", text)
        self.assertIn("DE: 10 → 80 (+70)", text)
        self.assertEqual(result["audit"]["explain"],
                         {"column": "Country", "period": "2025-05",
                          "comparedTo": "2025-04"})

    def test_first_period_anomaly_cannot_be_explained(self):
        result, port, _ = self.analyze(
            [self.series_rows(spike="2025-01")], flagged=("2025-01",),
            explain_by="Country")
        self.assertTrue(result["success"], result)
        self.assertEqual(len(port.queries), 1)
        self.assertIn("kıyaslanacak önceki dönem yok", str(result["insights"]))

    def test_invalid_shapes_are_rejected_before_sql(self):
        for overrides in [
            {"group_by": ["Country"]},
            {"having": {"op": ">", "value": 5}},
            {"window": "running_total"},
            {"measures": [{"column": "Amount", "aggregation": "sum", "label": "T"}]},
            {"period_compare": {"unit": "year"}},
            {"aggregation": "median"},
            {"explain_by": "Missing"},
        ]:
            with self.subTest(overrides=overrides):
                result, port, _ = self.analyze([self.series_rows()], **overrides)
                self.assertFalse(result["success"])
                self.assertEqual(port.queries, [])

    def test_short_history_fails_with_detector_message(self):
        result, _, _ = self.analyze([self.series_rows()[:5]])
        self.assertFalse(result["success"])
        self.assertIn("en az 8 dönem", result["error"])

    def test_without_time_axis_legacy_row_path_still_runs(self):
        result, port, _ = self.analyze([[]], time_axis=None)
        self.assertFalse(result["success"])
        self.assertIn("veri bulunamadı", result["error"])
        self.assertIn("SELECT", port.queries[0][0])


if __name__ == "__main__":
    unittest.main()
