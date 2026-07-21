"""
Schema-dogrulamali LLM cikti uretimi.

generate_structured: LLM'den JSON ister, pydantic ile dogrular; gecersizse
hatayi prompt'a ekleyip BIR kez daha dener, yine olmazsa LLMError firlatir.
"""
import json
import logging
import re
from typing import Type, TypeVar

from pydantic import BaseModel, ValidationError

from .client import LLMClient, LLMError

logger = logging.getLogger(__name__)

T = TypeVar('T', bound=BaseModel)


def _extract_json_text(text: str) -> str:
    cleaned = re.sub(r'```(?:json)?', '', text).strip().strip('`').strip()
    match = re.search(r'\{.*\}', cleaned, re.DOTALL)
    if not match:
        raise ValueError(f"Yanit icinde JSON bulunamadi: {text[:200]}")
    return match.group()


def generate_structured(client: LLMClient, system: str, messages: list[dict],
                        schema: Type[T], max_tokens: int = 2048) -> T:
    convo = list(messages)
    last_err = None
    for attempt in range(2):
        raw = client.generate(system, convo, temperature=0.0, max_tokens=max_tokens)
        try:
            return schema.model_validate_json(_extract_json_text(raw))
        except (ValidationError, ValueError, json.JSONDecodeError) as e:
            last_err = e
            logger.warning("Gecersiz structured yanit (deneme %d): %s", attempt + 1, str(e)[:300])
            convo = convo + [
                {'role': 'assistant', 'content': raw[:2000]},
                {'role': 'user', 'content':
                    f"Önceki yanıt geçersizdi: {str(e)[:400]}. "
                    "SADECE istenen şemaya uyan geçerli bir JSON nesnesi döndür, başka hiçbir şey yazma."},
            ]
    raise LLMError(f"Structured output {schema.__name__} uretilemedi: {last_err}")
