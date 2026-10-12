"""Time-axis bucketing and forecast regressions with dependency-free doubles."""

import importlib
import sys
import types
import unittest
from unittest.mock import patch

from query_spec import (
    Aggregate, ColumnRef, OrderBy, PeriodBucket, QuerySpec, QuerySpecError,
    TableRef, render, render_group_count,
)

# Keep the production environment dependency-free without leaking a pandas stub.
with patch.dict(sys.modules, {
    "pandas": types.SimpleNamespace(DataFrame=object, isna=lambda value: value is None),
}):
    AgentAnalyzer = importlib.import_module("agent_analyzer").AgentAnalyzer

from test_sql_aggregation import RecordingPort  # noqa: E402


class TimePort(RecordingPort):
    TABLES = {
        **RecordingPort.TABLES,
        "Calculations": ["Id", "ReferanceId", "ReferenceId", "Amount",
                         "CreatedDate", "Country"],
    }


class FakeForecastError(ValueError):
    pass


class FakeForecasterModule(types.ModuleType):
    """Stands in for `forecaster` so tests need neither numpy nor statsmodels."""

    def __init__(self):
        super().__init__("forecaster")
        self.ForecastError = FakeForecastError
        self.calls = []

    def forecast_series(self, series, horizon=12, seasonality=12):
        self.calls.append({"series": [dict(p) for p in series],
                           "horizon": horizon, "seasonality": seasonality})
        last = float(series[-1]["value"])
        forecast = [{"period": f"T+{i + 1}", "value": last,
                     "lower": last - 1, "upper": last + 1}
                    for i in range(horizon)]
        return {"forecast": forecast, "model_name": "FakeModel"}


class PeriodBucketSqlTests(unittest.TestCase):
    def spec(self, granularity):
        bucket = PeriodBucket("t0", "CreatedDate", "Donem", granularity)
        return QuerySpec(
            base=TableRef("dbo", "Calculations", "t0"),
            group_by=[bucket],
            aggregate=Aggregate("sum", ColumnRef("t0", "Amount"), "value"),
            order_by=[OrderBy(bucket, "asc")],
        )

    def test_granularities_render_sortable_period_text(self):
        for granularity, fragment in [
            ("year", "CONVERT(char(4), [t0].[CreatedDate], 126)"),
            ("month", "CONVERT(char(7), [t0].[CreatedDate], 126)"),
            ("day", "CONVERT(char(10), [t0].[CreatedDate], 126)"),
            ("quarter", "CONCAT(YEAR([t0].[CreatedDate]), '-Q', "
                        "(MONTH([t0].[CreatedDate]) + 2) / 3)"),
        ]:
            with self.subTest(granularity=granularity):
                sql = render(self.spec(granularity))
                self.assertIn(f"{fragment} AS [Donem]", sql)
                self.assertIn(f"GROUP BY {fragment}", sql)
                self.assertIn(f"ORDER BY {fragment} ASC", sql)

    def test_unknown_granularity_fails_before_sql(self):
        with self.assertRaises(QuerySpecError):
            render(self.spec("week"))

    def test_group_count_projection_names_the_period_column(self):
        sql = render_group_count(self.spec("month"))
        self.assertIn("AS [Donem]", sql)
        self.assertNotIn("GROUP BY CONVERT(char(7), [t0].[CreatedDate], 126) AS", sql)


class TimeAxisTests(unittest.TestCase):
    def analyze(self, rows, **overrides):
        port = TimePort(rows)
        analyzer = AgentAnalyzer(port)
        params = {"target_table": "dbo.Calculations", "aggregation": "sum",
                  "target_column": "Amount", "group_by": [], "chart_title": "",
                  "time_axis": {"column": "CreatedDate", "granularity": "month"}}
        params.update(overrides)
        config = {
            "tables": [{"name": f"dbo.{table}"} for table in port.TABLES],
            "columns": [{"table": f"dbo.{table}", "column": column}
                        for table, columns in port.TABLES.items() for column in columns],
            "relationships": [],
        }
        return analyzer.run_analysis(config, params), port

    def test_month_axis_builds_chronological_series_sql(self):
        result, port = self.analyze([
            {"Donem": "2026-01", "value": 10},
            {"Donem": "2026-02", "value": 20},
        ])
        self.assertTrue(result["success"], result)
        sql, params = port.queries[0]
        bucket = "CONVERT(char(7), [t0].[CreatedDate], 126)"
        self.assertIn(f"{bucket} AS [Donem]", sql)
        self.assertIn(f"GROUP BY {bucket}", sql)
        self.assertIn(f"ORDER BY {bucket} ASC", sql)
        self.assertIn("TOP (500)", sql)
        self.assertIn("[t0].[CreatedDate] IS NOT NULL", sql)
        self.assertEqual(params, {})
        self.assertEqual(result["charts"][0]["data"]["labels"], ["2026-01", "2026-02"])
        self.assertEqual(result["audit"]["timeAxis"],
                         {"column": "CreatedDate", "granularity": "month"})
        self.assertIn("İlk Dönem", str(result["insights"]))
        self.assertNotIn("En Yüksek", str(result["insights"]))

    def test_unknown_granularity_and_missing_column_are_rejected(self):
        for axis in [{"column": "CreatedDate", "granularity": "week"},
                     {"column": "Missing"}]:
            with self.subTest(axis=axis):
                result, port = self.analyze([], time_axis=axis)
                self.assertFalse(result["success"])
                self.assertEqual(port.queries, [])

    def test_extra_dimension_pivots_into_one_series_per_value(self):
        result, port = self.analyze([
            {"Donem": "2026-01", "Country": "DE", "value": 5},
            {"Donem": "2026-02", "Country": "DE", "value": 7},
            {"Donem": "2026-01", "Country": "NL", "value": 3},
        ], group_by=["Country"])
        self.assertTrue(result["success"], result)
        chart = result["charts"][0]["data"]
        self.assertEqual(chart["labels"], ["2026-01", "2026-02"])
        self.assertEqual(chart["datasets"], [
            {"label": "DE", "data": [5.0, 7.0]},
            # The NL/2026-02 group does not exist; a gap is honest, zero is not.
            {"label": "NL", "data": [3.0, None]},
        ])
        self.assertIn("GROUP BY CONVERT(char(7), [t0].[CreatedDate], 126), "
                      "[t0].[Country]", port.queries[0][0])

    def test_union_with_time_axis_is_rejected(self):
        result, port = self.analyze([], union={"with": [
            {"table": "dbo.ImportCalculations"}]})
        self.assertFalse(result["success"])
        self.assertIn("birleşim", result["error"].lower())
        self.assertEqual(port.queries, [])


class ForecastTests(unittest.TestCase):
    def analyze(self, rows, fake=None, **overrides):
        port = TimePort(rows)
        analyzer = AgentAnalyzer(port)
        params = {"target_table": "dbo.Calculations", "analysis_type": "forecast",
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
        fake = fake or FakeForecasterModule()
        with patch.dict(sys.modules, {"forecaster": fake}):
            return analyzer.run_analysis(config, params), port, fake

    @staticmethod
    def months(pairs):
        return [{"Donem": period, "value": value} for period, value in pairs]

    def test_forecast_fills_gaps_and_charts_history_plus_future(self):
        result, port, fake = self.analyze(self.months([
            ("2026-01", 10), ("2026-02", 20), ("2026-04", 40),
            ("2026-05", 50), ("2026-06", 60),
        ]), horizon=3)
        self.assertTrue(result["success"], result)

        self.assertEqual(len(fake.calls), 1)
        call = fake.calls[0]
        self.assertEqual(call["horizon"], 3)
        self.assertEqual(call["seasonality"], 12)
        self.assertEqual([p["period"] for p in call["series"]],
                         ["2026-01", "2026-02", "2026-03", "2026-04",
                          "2026-05", "2026-06"])
        self.assertEqual(call["series"][2]["value"], 0.0)

        chart = result["charts"][0]
        self.assertEqual(chart["type"], "line")
        data = chart["data"]
        self.assertEqual(len(data["labels"]), 9)
        self.assertEqual([d["label"] for d in data["datasets"]],
                         ["Gerçekleşen", "Tahmin", "Alt sınır (%95)", "Üst sınır (%95)"])
        actual, predicted, lower, upper = [d["data"] for d in data["datasets"]]
        self.assertEqual(actual[-3:], [None, None, None])
        self.assertEqual(actual[:2], [10.0, 20.0])
        # The bridge point keeps the line connected; it is the last actual.
        self.assertEqual(predicted[:5], [None, None, None, None, None])
        self.assertEqual(predicted[5], 60.0)
        self.assertEqual(lower[:6], [None] * 6)
        self.assertEqual(upper[6:], [61.0, 61.0, 61.0])

        self.assertEqual(result["audit"]["forecastModel"], "FakeModel")
        self.assertEqual(result["audit"]["filledGaps"], 1)
        self.assertEqual(result["audit"]["periodCount"], 6)
        self.assertEqual(result["audit"]["horizon"], 3)
        self.assertIn("Boş dönemler", str(result["insights"]))
        self.assertIn("öngörü", str(result["insights"]).lower())

    def test_forecast_requires_monthly_time_axis(self):
        for overrides in [
            {"time_axis": None},
            {"time_axis": {"column": "CreatedDate", "granularity": "year"}},
        ]:
            with self.subTest(overrides=overrides):
                result, port, fake = self.analyze([], **overrides)
                self.assertFalse(result["success"])
                self.assertEqual(port.queries, [])
                self.assertEqual(fake.calls, [])

    def test_forecast_rejects_breakdown_having_and_window(self):
        for overrides in [
            {"group_by": ["Country"]},
            {"having": {"op": ">", "value": 5}},
            {"window": "running_total"},
        ]:
            with self.subTest(overrides=overrides):
                result, port, fake = self.analyze([], **overrides)
                self.assertFalse(result["success"])
                self.assertEqual(port.queries, [])
                self.assertEqual(fake.calls, [])

    def test_forecast_needs_four_periods(self):
        result, port, fake = self.analyze(self.months([
            ("2026-01", 10), ("2026-02", 20), ("2026-03", 30),
        ]))
        self.assertFalse(result["success"])
        self.assertIn("en az 4", result["error"])
        self.assertEqual(fake.calls, [])

    def test_forecast_horizon_is_clamped_and_defaulted(self):
        rows = self.months([("2026-01", 1), ("2026-02", 2),
                            ("2026-03", 3), ("2026-04", 4)])
        for horizon, expected in [(99, 36), (0, 1), ("abc", 6), (None, 6)]:
            with self.subTest(horizon=horizon):
                result, _, fake = self.analyze(rows, horizon=horizon)
                self.assertTrue(result["success"], result)
                self.assertEqual(fake.calls[0]["horizon"], expected)

    def test_average_series_skips_empty_months_instead_of_inventing_zero(self):
        result, _, fake = self.analyze(self.months([
            ("2026-01", 10), ("2026-02", None), ("2026-03", 30),
            ("2026-04", 40), ("2026-05", 50),
        ]), aggregation="avg")
        self.assertTrue(result["success"], result)
        self.assertEqual([p["period"] for p in fake.calls[0]["series"]],
                         ["2026-01", "2026-03", "2026-04", "2026-05"])
        self.assertEqual(result["audit"]["filledGaps"], 0)


if __name__ == "__main__":
    unittest.main()
