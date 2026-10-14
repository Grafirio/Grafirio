import unittest
from datetime import date
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
    def setUp(self):
        clock = patch('forecast_history.today_utc', return_value=date(2026, 1, 15))
        clock.start()
        self.addCleanup(clock.stop)
    def test_identifier_measure_rejected_without_user_configuration(self):
        port = TimePort([])
        result = AgentAnalyzer(port).run_analysis(dictionary(), {"target_table": "dbo.Calculations",
            "target_column": "Id", "analysis_type": "aggregation", "aggregation": "sum"})
        self.assertFalse(result["success"])
        self.assertEqual(port.queries, [])
        self.assertEqual(result["audit"]["events"][-1]["stage"], "failed")

    def test_all_measures_and_qualified_identifiers_obey_general_rules(self):
        for params in [
            {"target_column": "dbo.Calculations.Id", "aggregation": "sum"},
            {"target_column": "Amount", "measures": [{"column": "Amount", "aggregation": "sum"}, {"column": "Id", "aggregation": "avg"}]},
        ]:
            with self.assertRaises(ValueError):
                check_metrics(dictionary(), {"target_table": "dbo.Calculations", **params})

    def test_missing_months_rejected_even_with_old_zero_rule(self):
        rows = [{"Donem": f"2025-{m:02}", "value": m * 10} for m in range(1, 13) if m != 3]
        params = {"target_table": "dbo.Calculations", "target_column": "Amount", "aggregation": "sum",
                  "analysis_type": "forecast", "horizon": 3, "time_axis": {"column": "CreatedDate", "granularity": "month"}}
        fake = FakeForecasterModule()
        with patch.dict("sys.modules", {"forecaster": fake}):
            result = AgentAnalyzer(TimePort(rows)).run_analysis(dictionary("zero"), params)
        self.assertFalse(result["success"])
        self.assertEqual(fake.calls, [])

    def test_five_year_request_returns_one_year_and_persisted_notice(self):
        rows = [{"Donem": f"2025-{m:02}", "value": m} for m in range(1, 13)]
        params = {"target_table": "dbo.Calculations", "target_column": "Amount", "aggregation": "sum",
                  "analysis_type": "forecast", "horizon": 60, "chart_title": "5 yıllık tahmin", "description": "5 yıllık sonuç",
                  "time_axis": {"column": "CreatedDate", "granularity": "month"}}
        config = dictionary(horizon=36)
        config["metricRules"] = []
        fake = FakeForecasterModule()
        with patch.dict("sys.modules", {"forecaster": fake}):
            result = AgentAnalyzer(TimePort(rows)).run_analysis(config, params)
        self.assertTrue(result["success"], result)
        self.assertEqual(fake.calls[0]["horizon"], 12)
        self.assertEqual(result["audit"]["requestedHorizon"], 60)
        self.assertEqual(result["audit"]["horizon"], 12)
        self.assertIn("5 yıl", result["report"]["notices"][0])
        self.assertIn("12 aylık", result["charts"][0]["title"])
        self.assertIn("12 aylık", result["summary"])
        self.assertEqual([len(t['rows']) for t in result['report']['tables']], [12, 12])
        self.assertEqual(result['report']['forecastSummary']['total'], 144.0)
        self.assertEqual(result['report']['forecastSummary']['previousTotal'], 78.0)
        self.assertEqual(result["report"]["quality"], "exploratory")

    def test_old_per_metric_rules_cannot_relax_general_history_limit(self):
        rows = [{"Donem": f"2026-{m:02}", "value": m} for m in range(1, 7)]
        fake = FakeForecasterModule()
        with patch.dict("sys.modules", {"forecaster": fake}):
            result = AgentAnalyzer(TimePort(rows)).run_analysis(dictionary(minimum=4), {
                "target_table": "dbo.Calculations", "target_column": "Amount", "aggregation": "sum",
                "analysis_type": "forecast", "horizon": 3, "time_axis": {"column": "CreatedDate", "granularity": "month"}})
        self.assertFalse(result["success"])
        self.assertEqual(fake.calls, [])
        self.assertIn("en az 12", result["error"])

    def test_explanation_comes_from_execution_not_llm_description(self):
        result = {"summary": "Hatalı iddia: sinir ağı ve yüzde yüz doğruluk", "insights": [],
                  "charts": [{"type": "bar", "title": "Tutar", "data": {"labels": ["A", "B"], "datasets": [{"label": "Toplam", "data": [0, None]}]}}]}
        report = build_report(result, {"analysisType": "aggregation", "aggregation": "sum", "targetTable": "dbo.Calculations"})
        self.assertNotIn("sinir ağı", report["method"])
        self.assertEqual(report["tables"][0]["rows"], [["A", 0], ["B", None]])
        before = report["snapshotHash"]
        result["charts"][0]["data"]["datasets"][0]["data"][0] = 9
        self.assertNotEqual(before, build_report(result, {})["snapshotHash"])
