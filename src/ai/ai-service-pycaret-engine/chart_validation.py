"""Validate the Chart.js subset supported by the client before publishing."""

import math

SUPPORTED_CHART_TYPES = frozenset({"bar", "line", "pie", "doughnut", "scatter", "table"})

#: Tablo cevabi grafik degil veri aktarimidir; tavan olmadan "listele"
#: cevabi dis katmanlara sinirsiz satir tasiyabilir.
MAX_TABLE_ROWS = 500


def validate_chart_type(chart_type):
    if not isinstance(chart_type, str) or chart_type not in SUPPORTED_CHART_TYPES:
        raise ValueError(f"Unsupported chart type: {chart_type!r}.")


def _number(value):
    return (isinstance(value, (int, float)) and not isinstance(value, bool)
            and math.isfinite(value))


def _validate_table(chart):
    data = chart.get("data")
    if not isinstance(data, dict):
        raise ValueError("Table data must be an object.")
    columns = data.get("columns")
    if (not isinstance(columns, list) or not columns
            or any(not isinstance(column, str) or not column for column in columns)):
        raise ValueError("Table requires nonempty string column names.")
    rows = data.get("rows")
    if not isinstance(rows, list) or len(rows) > MAX_TABLE_ROWS:
        raise ValueError("Table rows must be a list within the row cap.")
    for row in rows:
        if not isinstance(row, list) or len(row) != len(columns):
            raise ValueError("Each table row must match the column count.")
        for cell in row:
            if cell is not None and not isinstance(cell, str) and not _number(cell):
                raise ValueError("Table cells must be strings, finite numbers or null.")


def validate_charts(charts):
    if not isinstance(charts, list):
        raise ValueError("Charts must be a list.")
    for chart in charts:
        if not isinstance(chart, dict):
            raise ValueError("Each chart must be an object.")
        validate_chart_type(chart.get("type"))
        if not isinstance(chart.get("title"), str):
            raise ValueError("Chart title must be a string.")
        if chart["type"] == "table":
            _validate_table(chart)
            continue
        data = chart.get("data")
        if not isinstance(data, dict) or not isinstance(data.get("datasets"), list) or not data["datasets"]:
            raise ValueError("Chart data requires nonempty datasets.")
        scatter = chart["type"] == "scatter"
        labels = data.get("labels")
        if not scatter and (not isinstance(labels, list) or
                            any(not isinstance(label, str) for label in labels)):
            raise ValueError("Category charts require string labels.")
        for dataset in data["datasets"]:
            if not isinstance(dataset, dict) or not isinstance(dataset.get("label"), str):
                raise ValueError("Each dataset requires a label.")
            values = dataset.get("data")
            if not isinstance(values, list):
                raise ValueError("Dataset data must be a list.")
            if scatter:
                if any(not isinstance(point, dict) or not _number(point.get("x"))
                       or not _number(point.get("y")) for point in values):
                    raise ValueError("Scatter data requires finite numeric x/y points.")
            elif len(values) != len(labels) or any(value is not None and not _number(value) for value in values):
                raise ValueError("Dataset values must be finite numbers/null matching labels.")