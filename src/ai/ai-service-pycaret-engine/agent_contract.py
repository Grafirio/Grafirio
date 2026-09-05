"""Trusted backend request contract; config hashes cover the exact UTF-8 string."""

import hashlib
import json
from typing import Annotated

from pydantic import BaseModel, ConfigDict, StringConstraints, model_validator

Identifier = Annotated[str, StringConstraints(strict=True, min_length=1, max_length=128, pattern=r"^[A-Za-z0-9_-]+$")]
ConfigHash = Annotated[str, StringConstraints(strict=True, pattern=r"^[0-9a-f]{64}$")]
JsonString = Annotated[str, StringConstraints(strict=True, min_length=2, max_length=750000)]


def json_object(value):
    def reject_constant(constant):
        raise ValueError(f"Non-finite JSON value: {constant}")

    parsed = json.loads(value, parse_constant=reject_constant)
    if not isinstance(parsed, dict):
        raise ValueError("JSON must contain an object.")
    return parsed


class AgentAnalyzeRequest(BaseModel):
    model_config = ConfigDict(extra="forbid")

    request_id: Identifier
    query_id: Identifier
    company_id: Identifier
    connection_id: Identifier
    config_id: Identifier
    config_hash: ConfigHash
    config_json: JsonString
    analysis_params_json: JsonString
    user_question: Annotated[str, StringConstraints(strict=True, max_length=20000)]

    @model_validator(mode="after")
    def validate_config(self):
        if hashlib.sha256(self.config_json.encode("utf-8")).hexdigest() != self.config_hash:
            raise ValueError("config_hash does not match config_json.")
        json_object(self.config_json)
        json_object(self.analysis_params_json)
        return self