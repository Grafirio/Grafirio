"""No customer database or ML runtime is needed for the service boundary tests."""

import hashlib
import json
from unittest.mock import Mock, patch

import pytest
from fastapi.testclient import TestClient

from agent_analyzer import AgentAnalyzer
from agent_jobs import JobRunner
from chart_validation import validate_charts
from data_port import DataPortError, GatewayDataPort
from internal_auth import MAX_REQUEST_BYTES
from main import app

KEY = "test-service-key"
HEADERS = {"X-Grafirio-Internal-Key": KEY}


def payload(**changes):
    config = json.dumps({
        "tables": [{"name": "dbo.Orders"}, {"name": "dbo.Customers"}],
        "columns": [{"table": "dbo.Orders", "column": "CustomerId"},
                    {"table": "dbo.Orders", "column": "Amount"},
                    {"table": "dbo.Customers", "column": "Id"}],
    }, ensure_ascii=False)
    body = {"request_id": "request", "query_id": "query", "company_id": "company",
            "connection_id": "connection", "config_id": "config",
            "config_json": config, "config_hash": hashlib.sha256(config.encode("utf-8")).hexdigest(),
            "analysis_params_json": json.dumps({"target_table": "dbo.Orders"}),
            "user_question": "Total?"}
    body.update(changes)
    return body


def context(body=None):
    body = body or payload()
    return {key: body[key] for key in ("company_id", "connection_id", "config_id", "config_hash")}


@pytest.fixture
def client(tmp_path, monkeypatch):
    monkeypatch.setenv("INTERNAL_API_KEY", KEY)
    monkeypatch.setenv("AGENT_JOB_DB_PATH", str(tmp_path / "jobs.sqlite3"))
    with patch.object(JobRunner, "start"), TestClient(app) as test_client:
        yield test_client


@pytest.mark.parametrize("method,path", [
    ("GET", "/"), ("GET", "/docs"), ("GET", "/openapi.json"),
    ("POST", "/train"), ("POST", "/predict"), ("POST", "/forecast"),
    ("GET", "/models/company"), ("GET", "/train/status/company"),
    ("POST", "/agent/analyze"), ("GET", "/agent/analyze/status/query"),
    ("GET", "/agent/analyze/result/query"), ("POST", "/agent/analyze/cancel/query"),
    ("OPTIONS", "/agent/analyze"), ("POST", "/health"),
])
def test_every_non_health_route_is_authenticated(client, method, path):
    with patch("requests.post") as outbound:
        assert client.request(method, path).status_code == 401
        assert client.request(method, path, headers={"X-Grafirio-Internal-Key": "wrong"}).status_code == 401
        outbound.assert_not_called()


def test_missing_server_key_fails_closed_but_health_survives(client, monkeypatch):
    monkeypatch.delenv("INTERNAL_API_KEY")
    assert client.get("/health").status_code == 200
    assert client.post("/agent/analyze", json=payload(), headers=HEADERS).status_code == 503


def test_constant_time_comparison_and_duplicate_header_rejection(client):
    import internal_auth
    with patch.object(internal_auth.hmac, "compare_digest", wraps=internal_auth.hmac.compare_digest) as compare:
        assert client.get("/", headers=HEADERS).status_code == 200
        compare.assert_called_once_with(KEY.encode(), KEY.encode())
    assert client.get("/", headers=[("X-Grafirio-Internal-Key", KEY)] * 2).status_code == 401


@pytest.mark.parametrize("field", ["company_id", "connection_id", "query_id", "config_id", "config_hash"])
def test_submission_requires_context(client, field):
    body = payload()
    del body[field]
    assert client.post("/agent/analyze", json=body, headers=HEADERS).status_code == 422


@pytest.mark.parametrize("change", [
    {"config_hash": "a" * 64}, {"company_id": " "}, {"query_id": "../other"},
    {"analysis_params_json": "[]"}, {"analysis_params_json": '{"value": NaN}'},
    {"config_json": "{"}, {"unexpected": True},
])
def test_invalid_input_is_rejected(client, change):
    assert client.post("/agent/analyze", json=payload(**change), headers=HEADERS).status_code == 422


def test_exact_utf8_config_hash(client):
    config = json.dumps({"tables": [], "description": "İşlem ücreti"}, ensure_ascii=False)
    body = payload(config_json=config, config_hash=hashlib.sha256(config.encode("utf-8")).hexdigest())
    assert client.post("/agent/analyze", json=body, headers=HEADERS).status_code == 200
    body["config_json"] += " "
    assert client.post("/agent/analyze", json=body, headers=HEADERS).status_code == 422


def test_request_size_is_bounded(client):
    assert client.post("/agent/analyze", content=b"x" * (MAX_REQUEST_BYTES + 1), headers=HEADERS).status_code == 413


def test_idempotency_conflict_and_context_scoped_results(client):
    body = payload()
    first = client.post("/agent/analyze", json=body, headers=HEADERS)
    assert first.json()["status"] == "queued"
    assert client.post("/agent/analyze", json=body, headers=HEADERS).json() == first.json()
    assert client.post("/agent/analyze", json=payload(user_question="Changed"), headers=HEADERS).status_code == 409
    for field in context():
        wrong = {**context(), field: "b" * 64 if field == "config_hash" else "other"}
        for endpoint in ("status", "result"):
            assert client.get(f"/agent/analyze/{endpoint}/query", params=wrong, headers=HEADERS).status_code == 404
        assert client.post("/agent/analyze/cancel/query", params=wrong, headers=HEADERS).status_code == 404
    assert client.get("/agent/analyze/result/query", headers=HEADERS).status_code == 422
    assert client.get("/agent/analyze/result/query", params=context(), headers=HEADERS).status_code == 200
    assert client.post("/agent/analyze/cancel/query", params=context(), headers=HEADERS).json()["status"] == "cancelled"


def test_dataport_requires_context_and_forwards_camelcase(monkeypatch):
    monkeypatch.setenv("INTERNAL_API_KEY", KEY)
    body = payload()
    arguments = [body[key] for key in ("connection_id", "company_id", "query_id", "config_id", "config_hash")]
    port = GatewayDataPort(*arguments, base_url="http://backend")
    response = Mock(status_code=200)
    response.json.return_value = {"rows": [{"value": 1}]}
    with patch.object(port._requests, "post", return_value=response) as post:
        assert port.scalar("SELECT :value", {"value": 1}) == 1
        sent = post.call_args.kwargs
        assert sent["headers"] == HEADERS
        assert sent["json"] == {"connectionId": "connection", "companyId": "company",
                                "queryId": "query", "configId": "config", "configHash": body["config_hash"],
                                "sql": "SELECT @value", "parameters": {"value": 1}, "maxRows": 1}
    for index in range(len(arguments)):
        invalid = list(arguments)
        invalid[index] = ""
        with pytest.raises(DataPortError):
            GatewayDataPort(*invalid)
    monkeypatch.delenv("INTERNAL_API_KEY")
    with pytest.raises(DataPortError):
        GatewayDataPort(*arguments)


def configuration(pending=False):
    config = json.loads(payload()["config_json"])
    config["relationships"] = [{"fromTable": "dbo.Orders", "fromColumns": ["CustomerId"],
                                "toTable": "dbo.Customers", "toColumns": ["Id"],
                                "cardinality": "many-to-one", "needsConfirmation": pending}]
    return config


@pytest.mark.parametrize("params", [
    {"target_table": "dbo.Private"}, {"target_table": "other.dbo.Orders"},
    {"target_table": "dbo..Orders"},
    {"joins": [{"table": "dbo.Private"}]},
    {"union": {"with": [{"table": "dbo.Private"}]}},
    {"union": {"with": [{"table": "dbo.Customers", "joins": [{"table": "dbo.Private"}]}]}},
    {"target_column": "Secret", "aggregation": "sum"},
    {"filters": {"Secret": 1}}, {"chart_type": "heatmap"},
    {"analysis_type": "regression", "feature_columns": ["Secret"]},
])
def test_scope_rejection_precedes_all_io(params):
    data = Mock()
    result = AgentAnalyzer(data).run_analysis(configuration(), {"target_table": "dbo.Orders", **params})
    assert result["success"] is False
    data.read_sql.assert_not_called()
    data.scalar.assert_not_called()


@pytest.mark.parametrize("in_union", [False, True])
def test_pending_relationship_hard_stops_all_branches(in_union):
    params = {"target_table": "dbo.Orders", "joins": [{"table": "dbo.Customers"}]}
    if in_union:
        params = {"target_table": "dbo.Customers", "union": {"with": [
            {"table": "dbo.Orders", "joins": [{"table": "dbo.Customers"}]}]}}
    data = Mock()
    result = AgentAnalyzer(data).run_analysis(configuration(pending=True), params)
    assert result["success"] is False
    assert result["needsClarification"] is True
    assert result["pendingConfirmations"][0]["fromColumns"] == ["CustomerId"]
    data.read_sql.assert_not_called()
    data.scalar.assert_not_called()


def test_relationship_spelling_is_not_fuzzily_corrected():
    config = configuration()
    config["relationships"][0]["fromColumns"] = ["CustmerId"]
    data = Mock()
    result = AgentAnalyzer(data).run_analysis(config, {"target_table": "dbo.Orders", "joins": [{"table": "dbo.Customers"}]})
    assert not result["success"]
    data.read_sql.assert_not_called()


def test_canonical_table_and_selected_projection_do_not_expand_runtime_columns():
    config = configuration()
    analyzer = AgentAnalyzer(Mock())
    from analysis_scope import AnalysisScope
    from query_spec import TableRef
    analyzer._selection = AnalysisScope(config)
    assert analyzer._table_columns("DBO", "ORDERS") == {"customerid": "CustomerId", "amount": "Amount"}
    frame = Mock(columns=["CustomerId", "Amount"])
    frame.__len__ = Mock(return_value=1)
    analyzer.data.read_sql.return_value = frame
    analyzer._load_table_data(TableRef("dbo", "Orders", "t0"), [], {}, 10)
    sql = analyzer.data.read_sql.call_args.args[0]
    assert "[t0].[CustomerId], [t0].[Amount]" in sql
    assert ".*" not in sql
    assert "INFORMATION_SCHEMA" not in sql


@pytest.mark.parametrize("kind", ["bar", "line", "pie", "doughnut", "scatter"])
def test_supported_chart_schemas(kind):
    values = [{"x": 1, "y": 2}] if kind == "scatter" else [2]
    validate_charts([{"type": kind, "title": "Title", "data": {"labels": ["A"], "datasets": [{"label": "V", "data": values}]}}])


@pytest.mark.parametrize("kind,labels,values", [
    ("heatmap", ["A"], [1]), ("bar", ["A"], [float("nan")]),
    ("line", ["A"], [float("inf")]), ("pie", ["A"], [1, 2]),
    ("doughnut", ["A"], [True]), ("scatter", ["A"], [1]),
    ("scatter", [], [{"x": 1, "y": "2"}]),
])
def test_invalid_chart_outputs_fail_explicitly(kind, labels, values):
    analyzer = AgentAnalyzer(Mock())
    result = analyzer._with_audit({"success": True, "charts": [
        {"type": kind, "title": "Title", "data": {"labels": labels, "datasets": [{"label": "V", "data": values}]}}]})
    assert result["success"] is False