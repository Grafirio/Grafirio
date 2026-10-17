"""Generated regression inputs; no database, model or HTTP request is used."""
from datetime import date

from hypothesis import given, settings, strategies as st

from forecast_history import complete_month_history
from query_spec import (
    Aggregate, ColumnRef, PeriodBucket, Predicate, QuerySpec, TableRef, render,
)

# Bounded exploration for CI. Hypothesis still reports a minimized failing example.
settings.register_profile("grafirio", max_examples=200, deadline=None, derandomize=True)
settings.load_profile("grafirio")


@given(st.text(max_size=100))
def test_bound_filter_values_never_change_rendered_sql(value):
    # The production API keeps values outside QuerySpec in a separate binding dictionary.
    from data_port import GatewayDataPort
    from unittest.mock import Mock

    spec = QuerySpec(base=TableRef("dbo", "Items", "t0"),
                     aggregate=Aggregate("count", None, "value"),
                     where=[Predicate(ColumnRef("t0", "Label"), "=", ["p0"])])
    port = GatewayDataPort("connection", "company", "query", "config", "a" * 64,
                           base_url="http://127.0.0.1:1", api_key="synthetic-key")
    reply = Mock(status_code=200)
    reply.raise_for_status.return_value = None
    reply.json.return_value = {"columns": [], "rows": []}
    port._requests = Mock()
    port._requests.post.return_value = reply
    port.read_sql(render(spec), {"p0": value})
    body = port._requests.post.call_args.kwargs["json"]
    assert body["parameters"]["p0"] == value
    assert "@p0" in body["sql"]
    assert body["sql"] == "SELECT COUNT(*) AS [value] FROM [dbo].[Items] AS [t0] WHERE [t0].[Label] = @p0"


@given(st.dates(min_value=date(2000, 1, 1), max_value=date(2099, 12, 31)))
def test_forecast_history_never_includes_the_running_month(reference):
    period = PeriodBucket("t0", "CreatedDate", "Donem", "month")
    predicates, parameters, policy = complete_month_history(period, [], {}, reference_date=reference)
    boundary = reference.replace(day=1).isoformat()
    assert policy["toExclusive"] == boundary
    assert predicates[-1].op == "<"
    assert parameters[predicates[-1].param_keys[0]] == boundary


@given(st.text(alphabet=st.characters(min_codepoint=32, max_codepoint=126), min_size=1, max_size=60))
def test_column_identifiers_cannot_escape_their_brackets(name):
    spec = QuerySpec(base=TableRef("dbo", "Items", "t0"),
                     aggregate=Aggregate("sum", ColumnRef("t0", name), "value"))
    assert render(spec) == f"SELECT SUM([t0].[{name.replace(']', ']]')}]) AS [value] FROM [dbo].[Items] AS [t0]"
