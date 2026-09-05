"""Build an explicit dictionary whitelist without consulting runtime metadata."""

import re

_IDENTIFIER = re.compile(r"^[^\[\].\x00-\x1f]+$")
MAX_UNION_BRANCHES = 16


def table_parts(name):
    if not isinstance(name, str):
        raise ValueError("Table name must be a string.")
    parts = name.strip().split(".")
    if len(parts) not in (1, 2):
        raise ValueError("Only local schema.table names are supported.")
    parts = [part[1:-1] if part.startswith("[") and part.endswith("]") else part for part in parts]
    if any(not _IDENTIFIER.fullmatch(part) or part != part.strip() for part in parts):
        raise ValueError("Invalid table identifier.")
    return tuple(parts) if len(parts) == 2 else ("dbo", parts[0])


def canonical(name):
    return ".".join(table_parts(name)).casefold()


class AnalysisScope:
    def __init__(self, config):
        self.tables = {}
        entries = config.get("tables")
        if not isinstance(entries, list) or not entries:
            raise ValueError("config.tables must explicitly list selected tables.")
        for entry in entries:
            name = entry.get("name") if isinstance(entry, dict) else entry
            key = canonical(name)
            if key in self.tables:
                raise ValueError("Duplicate canonical table in config.tables.")
            self.tables[key] = {}
        columns = config.get("columns") or []
        if not isinstance(columns, list):
            raise ValueError("config.columns must be a list.")
        for entry in columns:
            if not isinstance(entry, dict):
                raise ValueError("Column entries must be objects.")
            key = canonical(entry.get("table"))
            if key not in self.tables:
                raise ValueError("Column table is outside config.tables.")
            name = entry.get("column")
            if not isinstance(name, str) or not _IDENTIFIER.fullmatch(name):
                raise ValueError("Invalid real column name in config.columns.")
            existing = self.tables[key].get(name.lower())
            if existing is not None and existing != name:
                raise ValueError("Ambiguous column spelling in config.columns.")
            self.tables[key][name.lower()] = name

    def columns(self, name):
        key = canonical(name)
        if key not in self.tables:
            raise ValueError(f"Table '{name}' is outside config.tables.")
        if not self.tables[key]:
            raise ValueError(f"No whitelisted columns for '{name}'.")
        return dict(self.tables[key])

    def preflight(self, analyzer, config, params, target):
        branches = [{"table": target, "joins": params.get("joins") or []}]
        union = params.get("union")
        if union:
            others = union if isinstance(union, list) else union.get("with") if isinstance(union, dict) else None
            if not isinstance(others, list) or not others or len(others) >= MAX_UNION_BRANCHES:
                raise ValueError("Invalid or oversized UNION branches.")
            branches.extend(others)

        # Validate every branch before resolving even the first relationship.
        for branch in branches:
            if not isinstance(branch, dict) or branch.get("union"):
                raise ValueError("Invalid or nested UNION branch.")
            self.columns(branch.get("table"))
            joins = branch.get("joins") or []
            if not isinstance(joins, list) or len(joins) > analyzer.MAX_JOINS:
                raise ValueError("Invalid or oversized join chain.")
            for step in joins:
                if not isinstance(step, dict):
                    raise ValueError("Each join must be an object.")
                self.columns(step.get("table"))

        for branch in branches:
            steps = {"base": branch["table"]}
            for index, step in enumerate(branch.get("joins") or []):
                name = str(step.get("as") or "").strip() or f"j{index}"
                source = str(step.get("from") or "base").strip()
                if source not in steps or name in steps:
                    raise ValueError("Invalid join source or duplicate alias.")
                edge, _ = analyzer._find_edge(config.get("relationships") or [], steps[source], step["table"], step.get("via"))
                left, right = edge.get("fromColumns"), edge.get("toColumns")
                if not isinstance(left, list) or not isinstance(right, list) or not left or len(left) != len(right):
                    raise ValueError("Relationship columns are incomplete.")
                for table, names in ((edge.get("fromTable"), left), (edge.get("toTable"), right)):
                    allowed = self.columns(table)
                    if any(not isinstance(column, str) or allowed.get(column.lower()) != column for column in names):
                        raise ValueError("Relationship must use exact whitelisted column names.")
                analyzer._record_pending(edge)
                steps[name] = step["table"]