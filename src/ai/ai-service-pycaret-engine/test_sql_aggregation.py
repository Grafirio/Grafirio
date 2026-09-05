"""SQL/global aggregate regressions with a dependency-free DataPort double."""

import importlib
import sqlite3
import sys
import types
import unittest
from unittest.mock import patch

from query_spec import (
    Aggregate, ColumnRef, HavingPredicate, Join, JoinCondition, ONE_TO_MANY,
    OrderBy, Predicate, QuerySpec, QuerySpecError, TableRef, WindowExpr,
    render, render_group_count,
)


# Keep the production environment dependency-free without leaking a pandas stub.
with patch.dict(sys.modules, {
    "pandas": types.SimpleNamespace(DataFrame=object, isna=lambda value: value is None),
}):
    AgentAnalyzer = importlib.import_module("agent_analyzer").AgentAnalyzer


class ResultColumn:
    def __init__(self, values):
        self.values = list(values)

    def tolist(self):
        return self.values

    def fillna(self, replacement):
        return ResultColumn([replacement if value is None else value for value in self.values])

    def __eq__(self, value):
        return [item == value for item in self.values]


class ResultFrame:
    def __init__(self, rows):
        self.rows = list(rows)
        self.empty = not self.rows
        self.iloc = self

    def __len__(self):
        return len(self.rows)

    def __getitem__(self, key):
        if isinstance(key, tuple):
            return ResultColumn([next(iter(row.values())) for row in self.rows])
        if isinstance(key, list):
            return ResultFrame([row for row, selected in zip(self.rows, key) if selected])
        return ResultColumn([row[key] for row in self.rows])

    def iterrows(self):
        return enumerate(self.rows)


class RecordingPort:
    TABLES = {
        "Calculations": ["Id", "ReferanceId", "ReferenceId", "Amount"],
        "ExportReferences": ["ReferenceId", "CustomerId"],
        "ImportCalculations": ["Id", "ReferenceId", "ReferanceId", "Amount"],
        "ImportReferences": ["ReferanceId", "CustomerId"],
        "Customers": ["Id", "Name", "CountryId"],
        "Countries": ["Id", "Name"],
    }

    def __init__(self, rows):
        self.rows = rows
        self.queries = []
        self.count_queries = []

    def read_sql(self, sql, params=None, max_rows=None):
        if "INFORMATION_SCHEMA.COLUMNS" in sql:
            return ResultFrame([{"COLUMN_NAME": column} for column in self.TABLES[params["table"]]])
        self.queries.append((sql, dict(params or {})))
        return ResultFrame(self.rows)

    def scalar(self, sql, params=None):
        self.count_queries.append((sql, dict(params or {})))
        return len(self.rows)


def declared_edge(source, source_column, target, target_column):
    return {
        "fromTable": f"dbo.{source}", "fromColumns": [source_column],
        "toTable": f"dbo.{target}", "toColumns": [target_column],
        "source": "declared", "cardinality": "many-to-one",
        "isTrusted": True, "isOptional": False, "needsConfirmation": False,
    }


EXPORT_EDGE = declared_edge("Calculations", "ReferanceId", "ExportReferences", "ReferenceId")
IMPORT_EDGE = declared_edge("ImportCalculations", "ReferenceId", "ImportReferences", "ReferanceId")
CUSTOMER_EDGE = declared_edge("ExportReferences", "CustomerId", "Customers", "Id")
COUNTRY_EDGE = declared_edge("Customers", "CountryId", "Countries", "Id")
CHAIN = [
    {"as": "reference", "from": "base", "table": "dbo.ExportReferences"},
    {"as": "customer", "from": "reference", "table": "dbo.Customers"},
    {"as": "country", "from": "customer", "table": "dbo.Countries"},
]


class GlobalSqlTests(unittest.TestCase):
    def setUp(self):
        self.base = TableRef("dbo", "Calculations", "t0")
        self.amount = ColumnRef("t0", "Amount")
        self.measure = Aggregate("sum", self.amount, "value")

    def spec(self, **overrides):
        fields = {"base": self.base, "aggregate": self.measure,
                  "having": [HavingPredicate(self.measure, ">", ["h0"])]}
        fields.update(overrides)
        return QuerySpec(**fields)

    def test_global_having_renders_without_group(self):
        self.assertEqual(render(self.spec()),
                         "SELECT SUM([t0].[Amount]) AS [value] "
                         "FROM [dbo].[Calculations] AS [t0] HAVING SUM([t0].[Amount]) > :h0")

    def test_global_top_needs_no_tie_breaker(self):
        self.assertIn("SELECT TOP (1) SUM", render(self.spec(limit=1)))

    def test_global_where_and_multiple_having_measures(self):
        sql = render(self.spec(
            where=[Predicate(self.amount, ">", ["p0"])],
            having=[HavingPredicate(self.measure, ">", ["h0"]),
                    HavingPredicate(Aggregate("count", None, "count"), "<", ["h1"])]))
        self.assertIn("WHERE [t0].[Amount] > :p0 HAVING SUM([t0].[Amount]) > :h0 AND COUNT(*) < :h1", sql)

    def test_scalar_semantics_and_group_count_on_sqlite_subset(self):
        # This executes the shared SQL subset, not a SQL Server integration test.
        with sqlite3.connect(":memory:") as connection:
            connection.execute("ATTACH DATABASE ':memory:' AS dbo")
            connection.execute("CREATE TABLE dbo.Calculations (Amount INTEGER)")
            connection.executemany("INSERT INTO dbo.Calculations VALUES (?)", [(10,), (20,)])
            for threshold, expected in [(25, [(30,)]), (35, [])]:
                with self.subTest(threshold=threshold):
                    self.assertEqual(connection.execute(render(self.spec()), {"h0": threshold}).fetchall(), expected)
                    self.assertEqual(connection.execute(render_group_count(self.spec()), {"h0": threshold}).fetchone(), (len(expected),))
            connection.execute("DELETE FROM dbo.Calculations")
            count = self.spec(aggregate=Aggregate("count", None, "value"), having=[])
            self.assertEqual(connection.execute(render(count)).fetchall(), [(0,)])
            self.assertEqual(connection.execute(render(self.spec(having=[]))).fetchall(), [(None,)])
            self.assertEqual(connection.execute(render_group_count(self.spec(having=[]))).fetchone(), (1,))

    def test_naked_columns_remain_guarded(self):
        for overrides in [
            {"select_all": True},
            {"order_by": [OrderBy(self.amount)]},
            {"order_by": [OrderBy("Amount")]},
            {"having": [Predicate(self.amount, ">", ["h0"])]},
            {"windows": [WindowExpr("rank", "rank", order_by=[OrderBy(self.amount)])]},
        ]:
            with self.subTest(overrides=overrides), self.assertRaises(QuerySpecError):
                render(self.spec(**overrides))

    def test_grouped_naked_ordering_remains_guarded(self):
        with self.assertRaises(QuerySpecError):
            render(self.spec(group_by=[ColumnRef("t0", "Id")], order_by=[OrderBy(self.amount)]))

    def test_aggregate_ordering_alias_scope_is_checked(self):
        with self.assertRaises(QuerySpecError):
            render(self.spec(order_by=[OrderBy(Aggregate("sum", ColumnRef("missing", "Amount"), "other"))]))

    def test_valid_aggregate_ordering(self):
        for target in ["value", self.measure]:
            with self.subTest(target=target):
                self.assertIn("ORDER BY", render(self.spec(order_by=[OrderBy(target)])))

    def test_global_unsafe_join_remains_guarded(self):
        with self.assertRaisesRegex(QuerySpecError, "bire-çok"):
            render(self.spec(joins=[Join(TableRef("dbo", "Lines", "t1"), [
                JoinCondition(ColumnRef("t0", "Id"), ColumnRef("t1", "CalculationId"))
            ], cardinality=ONE_TO_MANY)]))

    def test_global_having_unknown_alias_remains_guarded(self):
        with self.assertRaisesRegex(QuerySpecError, "tanımlı değil"):
            render(self.spec(having=[HavingPredicate(
                Aggregate("sum", ColumnRef("unknown", "Amount"), "other"), ">", ["h0"])]))

    def test_group_count_ignores_projection_windows_top_and_order(self):
        sql = render_group_count(self.spec(
            limit=1, order_by=[OrderBy("value")],
            windows=[WindowExpr("sum", "total", over=self.measure)]))
        self.assertIn("HAVING SUM([t0].[Amount]) > :h0", sql)
        for fragment in ["TOP", "ORDER BY", "OVER", "GROUP BY"]:
            self.assertNotIn(fragment, sql)

    def test_row_projection_cannot_be_used_as_group_count(self):
        with self.assertRaises(QuerySpecError):
            render_group_count(QuerySpec(base=self.base, select_all=True))


class AnalyzerSqlTests(unittest.TestCase):
    def analyze(self, rows, relationships=None, **overrides):
        port = RecordingPort(rows)
        analyzer = AgentAnalyzer(port)
        params = {"target_table": "dbo.Calculations", "aggregation": "sum",
                  "target_column": "Amount", "group_by": [], "chart_title": ""}
        params.update(overrides)
        config = {
            "tables": [{"name": f"dbo.{table}"} for table in port.TABLES],
            "columns": [{"table": f"dbo.{table}", "column": column}
                        for table, columns in port.TABLES.items() for column in columns],
            "relationships": relationships or [],
        }
        return analyzer.run_analysis(config, params), port

    def test_global_result_and_parameterized_having(self):
        result, port = self.analyze([{"value": 1250}], having={"op": ">", "value": 1000},
                                    filters={"Amount": {"gt": 10}})
        self.assertTrue(result["success"], result)
        sql, params = port.queries[0]
        self.assertNotIn("GROUP BY", sql)
        self.assertNotIn("TOP", sql)
        self.assertNotIn("ORDER BY", sql)
        self.assertEqual(params, {"p0": 10, "h0": 1000})
        self.assertEqual(result["audit"]["resolvedGroupBy"], [])
        self.assertEqual(result["audit"]["groupCount"], 1)
        self.assertEqual(result["charts"][0]["data"]["datasets"][0]["data"], [1250.0])
        self.assertEqual(len(result["charts"][0]["data"]["labels"]), 1)
        self.assertEqual(port.count_queries, [])
        self.assertNotIn("En Yüksek", str(result["insights"]))

    def test_global_empty_count_and_null_numeric_aggregates(self):
        for aggregation, value in [("count", 0), ("sum", None), ("avg", None), ("min", None), ("max", None)]:
            with self.subTest(aggregation=aggregation):
                result, _ = self.analyze([{"value": value}], aggregation=aggregation,
                                         target_column=None if aggregation == "count" else "Amount")
                self.assertTrue(result["success"], result)
                self.assertEqual(result["charts"][0]["data"]["datasets"][0]["data"], [value])

    def test_global_having_false_is_not_charted_as_zero(self):
        result, port = self.analyze([], having={"op": "<", "value": 5})
        self.assertFalse(result["success"])
        self.assertEqual(result["audit"]["groupCount"], 0)
        self.assertIn("koşul", result["error"])
        self.assertNotIn("düşür", result["error"])
        self.assertEqual(len(port.queries), 1)

    def test_global_missing_target_remains_guarded(self):
        for target in [None, "Missing"]:
            with self.subTest(target=target):
                result, port = self.analyze([], target_column=target)
                self.assertFalse(result["success"])
                self.assertEqual(port.queries, [])

    def test_global_running_total_remains_guarded(self):
        result, port = self.analyze([], window="running_total")
        self.assertFalse(result["success"])
        self.assertIn("kırılım", result["error"])
        self.assertEqual(port.queries, [])

    def test_global_total_window_preserves_null(self):
        result, port = self.analyze([{"value": None, "total": None}],
                                    window={"function": "total", "label": "total"})
        self.assertTrue(result["success"], result)
        self.assertIn("SUM(SUM([t0].[Amount])) OVER () AS [total]", port.queries[0][0])
        self.assertEqual([dataset["data"] for dataset in result["charts"][0]["data"]["datasets"]],
                         [[None], [None]])

    def test_grouped_having_keeps_count_filters_and_chart(self):
        result, port = self.analyze([{"ReferenceId": "A", "value": 30}],
                                    group_by=["ReferenceId"], filters={"Amount": {"gt": 1}},
                                    having={"op": ">", "value": 10})
        self.assertTrue(result["success"], result)
        self.assertEqual(len(port.count_queries), 1)
        for sql, params in port.queries + port.count_queries:
            self.assertIn("GROUP BY [t0].[ReferenceId]", sql)
            self.assertIn("WHERE [t0].[Amount] > :p0", sql)
            self.assertIn("HAVING SUM([t0].[Amount]) > :h0", sql)
            self.assertEqual(params, {"p0": 1, "h0": 10})
        self.assertEqual(result["charts"][0]["data"]["labels"], ["A"])
        self.assertIn("En Yüksek", str(result["insights"]))

    def test_empty_relationships_offer_declaration_but_never_execute(self):
        result, port = self.analyze([], joins=CHAIN[:1])
        self.assertFalse(result["success"])
        self.assertIn("Analiz Et", result["error"])
        self.assertIn("onay", result["error"])
        self.assertIn("doğrulama", result["error"])
        self.assertEqual(port.queries, [])

    def test_three_and_four_table_chains(self):
        for count, group in [(2, "customer.Name"), (3, "country.Name")]:
            with self.subTest(tables=count + 1):
                result, port = self.analyze([{"Name": "Example", "value": 30}],
                    [EXPORT_EDGE, CUSTOMER_EDGE, COUNTRY_EDGE], joins=CHAIN[:count], group_by=[group])
                self.assertTrue(result["success"], result)
                sql = port.queries[0][0]
                self.assertEqual(sql.count("LEFT JOIN"), count)
                self.assertIn("[t0].[ReferanceId] = [t1].[ReferenceId]", sql)
                self.assertIn("[t1].[CustomerId] = [t2].[Id]", sql)
                if count == 3:
                    self.assertIn("[t2].[CountryId] = [t3].[Id]", sql)
                self.assertIn(f"GROUP BY [t{count}].[Name]", sql)
                self.assertEqual(result["audit"]["evidence"], "declared")
                self.assertNotIn("pendingConfirmations", result["audit"])
                self.assertEqual(port.count_queries[0][0].count("LEFT JOIN"), count)

    def test_global_declared_chain(self):
        result, port = self.analyze([{"value": 30}], [EXPORT_EDGE, CUSTOMER_EDGE, COUNTRY_EDGE],
                                    joins=CHAIN, having={"op": ">", "value": 20})
        self.assertTrue(result["success"], result)
        self.assertEqual(port.queries[0][0].count("LEFT JOIN"), 3)
        self.assertNotIn("GROUP BY", port.queries[0][0])

    def test_missing_chain_edge_does_not_fabricate_join(self):
        result, port = self.analyze([], [EXPORT_EDGE, COUNTRY_EDGE], joins=CHAIN)
        self.assertFalse(result["success"])
        self.assertEqual(port.queries, [])
        self.assertIn("dbo.ExportReferences", result["error"])
        self.assertIn("dbo.Customers", result["error"])
        self.assertIn("kolon", result["error"])
        self.assertIn("onay", result["error"].lower())
        self.assertNotIn("tek tablo", result["error"])
        self.assertNotIn("yeterli", result["error"])

    def test_missing_edge_via_is_not_fuzzy_matched(self):
        result, port = self.analyze([], [EXPORT_EDGE], joins=[{**CHAIN[0], "via": "ReferenseId"}])
        self.assertFalse(result["success"])
        self.assertEqual(port.queries, [])

    def test_union_uses_distinct_declared_column_names_per_branch(self):
        result, port = self.analyze([
            {"ReferenceId": "A", "value": 30, "Kaynak": "Export"},
            {"ReferenceId": "A", "value": 20, "Kaynak": "Import"},
        ], [EXPORT_EDGE, IMPORT_EDGE],
            joins=[{**CHAIN[0], "filter": {"CustomerId": 7}}],
            group_by=["reference.ReferenceId"], filters={"Amount": {"gt": 1}},
            having={"op": ">", "value": 10},
            union={"label": "Export", "with": [{
                "table": "dbo.ImportCalculations", "label": "Import",
                "group_by": ["reference.ReferanceId"], "filters": {"Amount": {"gt": 2}},
                "joins": [{"as": "reference", "table": "dbo.ImportReferences", "filter": {"CustomerId": 8}}],
            }]})
        self.assertTrue(result["success"], result)
        sql, params = port.queries[0]
        self.assertIn("[t0].[ReferanceId] = [t1].[ReferenceId]", sql)
        self.assertIn("[t2].[ReferenceId] = [t3].[ReferanceId]", sql)
        self.assertIn("[t3].[ReferanceId] AS [ReferenceId]", sql)
        self.assertEqual(sql.count("LEFT JOIN"), 2)
        self.assertEqual(sql.count("HAVING"), 2)
        self.assertIn("UNION ALL", sql)
        self.assertEqual(params, {"ju0_0_0": 7, "u0_0": 1, "src0": "Export", "hu0_0": 10,
                                  "ju1_0_0": 8, "u1_0": 2, "src1": "Import", "hu1_0": 10})
        self.assertEqual(result["audit"]["evidence"], "declared")
        self.assertEqual([dataset["data"] for dataset in result["charts"][0]["data"]["datasets"]], [[30.0], [20.0]])

    def test_union_missing_branch_edge_is_not_borrowed(self):
        result, port = self.analyze([], [EXPORT_EDGE], joins=CHAIN[:1], group_by=["reference.ReferenceId"],
            union={"with": [{"table": "dbo.ImportCalculations", "group_by": ["reference.ReferanceId"],
                              "joins": [{"as": "reference", "table": "dbo.ImportReferences"}]}]})
        self.assertFalse(result["success"])
        self.assertEqual(port.queries, [])
        self.assertIn("dbo.ImportCalculations", result["error"])
        self.assertIn("dbo.ImportReferences", result["error"])


if __name__ == "__main__":
    unittest.main()