import unittest
from unittest.mock import patch
from analysis_report import check_metrics, build_report
from test_forecast import AgentAnalyzer, TimePort, FakeForecasterModule


def dictionary(policy="reject", minimum=4, horizon=6):
    return {"tables": [{"name": "dbo.Calculations"}],
            "columns": [{"table": "dbo.Calculations", "column": c,
                         "role": "identifier" if c == "Id" else "other"}
                        for c in TimePort.TABLES["Calculations"]],
            "relationships": [], "metricRules": [{"table": "dbo.Calculations", "column": "Amount",
                "label": "Tutar", "unit": "TL", "allowedAggregations": ["sum"], "forecastEnabled": True,
                "dateColumn": "CreatedDate", "missingPeriodPolicy": policy,
                "minHistoryPeriods": minimum, "maxHorizon": horizon}]}


class ReportTests(unittest.TestCase):
    def test_rejected_metric_never_reads_data_and_keeps_trace(self):
        port = TimePort([])
        result = AgentAnalyzer(port).run_analysis(dictionary(), {"target_table": "dbo.Calculations",
            "target_column": "Amount", "analysis_type": "aggregation", "aggregation": "avg"})
        self.assertFalse(result["success"])
        self.assertEqual(port.queries, [])
        self.assertEqual(result["audit"]["errorCode"], "metric_validation_failed")
        self.assertEqual(result["audit"]["events"][-1]["stage"], "failed")

    def test_all_measures_and_qualified_columns_obey_rules(self):
        for params in [
            {"target_column": "dbo.Calculations.Amount", "aggregation": "avg"},
            {"target_column": "Amount", "measures": [{"column": "Amount", "aggregation": "sum"}, {"column": "Amount", "aggregation": "avg"}]},
            {"target_column": "Id", "aggregation": "sum"},
        ]:
            with self.assertRaises(ValueError):
                check_metrics(dictionary(), {"target_table": "dbo.Calculations", **params})

    def test_missing_months_rejected_unless_explicitly_zero(self):
        rows = [{"Donem": f"2026-{m:02}", "value": m * 10} for m in (1, 2, 4, 5, 6)]
        params = {"target_table": "dbo.Calculations", "target_column": "Amount", "aggregation": "sum",
                  "analysis_type": "forecast", "horizon": 3,
                  "time_axis": {"column": "CreatedDate", "granularity": "month"}}
        for policy, success in [("reject", False), ("zero", True)]:
            fake = FakeForecasterModule()
            with patch.dict("sys.modules", {"forecaster": fake}):
                result = AgentAnalyzer(TimePort(rows)).run_analysis(dictionary(policy), params)
            self.assertEqual(result["success"], success)
            self.assertEqual(bool(fake.calls), success)
            if success:
                self.assertEqual(result["report"]["dataPeriod"], {"from": "2026-01", "to": "2026-06"})
                self.assertEqual(result["report"]["quality"], "exploratory")
                self.assertEqual(len(result["report"]["tables"][0]["rows"]), 9)
                self.assertTrue(any(e["stage"] == "data_read" for e in result["audit"]["events"]))

    def test_history_and_horizon_gates_prevent_model_execution(self):
        rows = [{"Donem": f"2026-{m:02}", "value": m} for m in range(1, 7)]
        params = {"target_table": "dbo.Calculations", "target_column": "Amount", "aggregation": "sum",
                  "analysis_type": "forecast", "horizon": 7, "time_axis": {"column": "CreatedDate", "granularity": "month"}}
        for config in (dictionary(horizon=6), dictionary(minimum=12, horizon=12)):
            fake = FakeForecasterModule()
            with patch.dict("sys.modules", {"forecaster": fake}):
                result = AgentAnalyzer(TimePort(rows)).run_analysis(config, params)
            self.assertFalse(result["success"])
            self.assertEqual(fake.calls, [])

    def test_explanation_comes_from_execution_not_llm_description(self):
        result = {"summary": "Hatalı iddia: sinir ağı ve yüzde yüz doğruluk", "insights": [],
                  "charts": [{"type": "bar", "title": "Tutar", "data": {"labels": ["A", "B"], "datasets": [{"label": "Toplam", "data": [0, None]}]}}]}
        report = build_report(result, {"analysisType": "aggregation", "aggregation": "sum", "targetTable": "dbo.Calculations"})
        self.assertNotIn("sinir ağı", report["method"])
        self.assertEqual(report["tables"][0]["rows"], [["A", 0], ["B", None]])
        before = report["snapshotHash"]
        result["charts"][0]["data"]["datasets"][0]["data"][0] = 9
        self.assertNotEqual(before, build_report(result, {})["snapshotHash"])
