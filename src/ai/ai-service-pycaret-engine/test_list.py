"""Row-listing (`list`) answers: SQL shape, table payload and guards."""

import importlib
import sys
import types
import unittest
from unittest.mock import patch

from chart_validation import validate_chart_type, validate_charts

# Keep the production environment dependency-free without leaking a pandas stub.
with patch.dict(sys.modules, {
    "pandas": types.SimpleNamespace(DataFrame=object, isna=lambda value: value is None),
}):
    AgentAnalyzer = importlib.import_module("agent_analyzer").AgentAnalyzer

from test_forecast import TimePort  # noqa: E402


class TableValidationTests(unittest.TestCase):
    @staticmethod
    def table(rows, columns=("Tarih", "Tutar")):
        return [{"type": "table", "title": "Liste",
                 "data": {"columns": list(columns), "rows": rows}}]

    def test_table_type_and_payload_are_accepted(self):
        validate_chart_type("table")
        validate_charts(self.table([["2026-01-02", 12.5], ["2026-01-01", None]]))

    def test_malformed_tables_are_rejected(self):
        for rows, columns in [
            ([["tek"]], ("Tarih", "Tutar")),          # satir genisligi tutmuyor
            ([["t", True]], ("Tarih", "Tutar")),      # bool hucre
            ([["t", float("inf")]], ("Tarih", "Tutar")),
            ([], ()),                                  # kolon yok
            ([[None] * 2] * 501, ("A", "B")),          # satir tavani
        ]:
            with self.subTest(columns=columns, rows=len(rows)):
                with self.assertRaises(ValueError):
                    validate_charts(self.table(rows, columns))


class ListAnalysisTests(unittest.TestCase):
    ROWS = [
        {"CreatedDate": "2026-02-01", "Country": "DE", "Amount": 12.5},
        {"CreatedDate": "2026-01-15", "Country": None, "Amount": None},
    ]

    def analyze(self, rows, **overrides):
        port = TimePort(rows)
        analyzer = AgentAnalyzer(port)
        params = {"target_table": "dbo.Calculations", "analysis_type": "list",
                  "columns": ["CreatedDate", "Country", "Amount"],
                  "sort_by": "CreatedDate", "sort_order": "desc",
                  "limit": 3, "chart_title": "", "group_by": []}
        params.update(overrides)
        config = {
            "tables": [{"name": f"dbo.{table}"} for table in port.TABLES],
            "columns": [{"table": f"dbo.{table}", "column": column}
                        for table, columns in port.TABLES.items() for column in columns],
            "relationships": [],
        }
        return analyzer.run_analysis(config, params), port

    def test_list_returns_ordered_table_payload(self):
        result, port = self.analyze(self.ROWS)
        self.assertTrue(result["success"], result)

        sql, params = port.queries[0]
        self.assertIn("SELECT TOP (3) [t0].[CreatedDate], [t0].[Country], "
                      "[t0].[Amount]", sql)
        # Esitlik bozulmasin: siralama kolonundan sonra gosterilen kolonlar.
        self.assertIn("ORDER BY [t0].[CreatedDate] DESC, [t0].[Country] ASC, "
                      "[t0].[Amount] ASC", sql)
        self.assertNotIn("GROUP BY", sql)
        self.assertEqual(params, {})
        self.assertEqual(len(port.count_queries), 1)
        self.assertIn("COUNT(*)", port.count_queries[0][0])

        chart = result["charts"][0]
        self.assertEqual(chart["type"], "table")
        self.assertEqual(chart["data"]["columns"],
                         ["CreatedDate", "Country", "Amount"])
        self.assertEqual(chart["data"]["rows"], [
            ["2026-02-01", "DE", 12.5],
            ["2026-01-15", None, None],
        ])
        self.assertEqual(result["audit"]["listColumns"],
                         ["CreatedDate", "Country", "Amount"])
        self.assertEqual(result["audit"]["totalRows"], 2)
        self.assertIn("2 satır gösteriliyor (toplam 2)", str(result["insights"]))

    def test_limit_is_clamped_and_defaulted(self):
        for limit, expected in [(500, "TOP (100)"), ("abc", "TOP (20)"),
                                (None, "TOP (20)")]:
            with self.subTest(limit=limit):
                result, port = self.analyze(self.ROWS, limit=limit)
                self.assertTrue(result["success"], result)
                self.assertIn(expected, port.queries[0][0])

    def test_missing_pieces_are_rejected_before_sql(self):
        for overrides in [
            {"columns": None},
            {"columns": []},
            {"columns": [f"C{i}" for i in range(11)]},
            {"columns": ["Missing"]},
            {"sort_by": None},
            {"sort_by": "Missing"},
        ]:
            with self.subTest(overrides=overrides):
                result, port = self.analyze(self.ROWS, **overrides)
                self.assertFalse(result["success"])
                self.assertEqual(port.queries, [])

    def test_list_rejects_summary_shapes(self):
        for overrides in [
            {"having": {"op": ">", "value": 5}},
            {"window": "running_total"},
            {"measures": [{"column": "Amount", "aggregation": "sum", "label": "T"}]},
            {"time_axis": {"column": "CreatedDate", "granularity": "month"}},
            {"period_compare": {"unit": "year"}},
            {"union": {"with": [{"table": "dbo.ImportCalculations"}]}},
        ]:
            with self.subTest(overrides=overrides):
                result, port = self.analyze(self.ROWS, **overrides)
                self.assertFalse(result["success"])
                self.assertEqual(port.queries, [])

    def test_empty_result_is_an_explicit_error(self):
        result, _ = self.analyze([])
        self.assertFalse(result["success"])
        self.assertIn("kayıt bulunamadı", result["error"])


if __name__ == "__main__":
    unittest.main()
