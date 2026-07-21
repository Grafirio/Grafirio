"""
Provider-bagimsiz LLM istemcisi.

LLM_PROVIDER env degiskeni ile secilir:
  - azure_openai : Azure OpenAI chat-completions uyumlu deployment
  - gemini       : google-generativeai (varsayilan — Azure hazir olana kadar dev ortami)

Structured output provider'in JSON moduna BIRAKILMAZ; dogrulama her zaman
kod tarafinda (llm/structured.py, pydantic) yapilir.
"""
import logging
import os
import time

import requests

logger = logging.getLogger(__name__)


class LLMError(RuntimeError):
    """LLM cagrisi kalici olarak basarisiz oldugunda firlatilir."""


class LLMClient:
    def __init__(self, provider: str | None = None):
        self.provider = (provider or os.getenv('LLM_PROVIDER', 'gemini')).strip().lower()
        if self.provider not in ('azure_openai', 'gemini'):
            raise LLMError(f"Bilinmeyen LLM_PROVIDER: {self.provider}")

    def generate(self, system: str, messages: list[dict],
                 temperature: float = 0.0, max_tokens: int = 2048) -> str:
        """
        messages: [{'role': 'user'|'assistant', 'content': str}, ...]
        Doner: modelin metin yaniti. 429/5xx'te 1 retry, sonra LLMError.
        """
        last_err = None
        for attempt in range(2):
            try:
                if self.provider == 'azure_openai':
                    return self._generate_azure(system, messages, temperature, max_tokens)
                return self._generate_gemini(system, messages, temperature, max_tokens)
            except _RetryableError as e:
                last_err = e
                if attempt == 0:
                    wait = e.retry_after or 5
                    logger.warning("LLM gecici hata (%s), %ds sonra retry", e, wait)
                    time.sleep(min(wait, 30))
            except LLMError:
                raise
            except Exception as e:
                raise LLMError(f"LLM cagrisi basarisiz: {e}") from e
        raise LLMError(f"LLM cagrisi retry sonrasi da basarisiz: {last_err}")

    # ── Azure OpenAI ─────────────────────────────────────────────────────
    def _generate_azure(self, system, messages, temperature, max_tokens) -> str:
        endpoint = os.getenv('AZURE_OPENAI_ENDPOINT', '').rstrip('/')
        deployment = os.getenv('AZURE_OPENAI_DEPLOYMENT', '')
        api_key = os.getenv('AZURE_OPENAI_API_KEY', '')
        api_version = os.getenv('AZURE_OPENAI_API_VERSION', '2024-06-01')
        if not (endpoint and deployment and api_key):
            raise LLMError("AZURE_OPENAI_ENDPOINT / AZURE_OPENAI_DEPLOYMENT / AZURE_OPENAI_API_KEY eksik")

        url = f"{endpoint}/openai/deployments/{deployment}/chat/completions?api-version={api_version}"
        payload = {
            'messages': [{'role': 'system', 'content': system}] + [
                {'role': m['role'] if m['role'] in ('user', 'assistant') else 'user',
                 'content': m['content']}
                for m in messages
            ],
            'temperature': temperature,
            'max_tokens': max_tokens,
        }
        resp = requests.post(url, json=payload, headers={'api-key': api_key}, timeout=90)
        if resp.status_code == 429 or resp.status_code >= 500:
            raise _RetryableError(f"HTTP {resp.status_code}",
                                  retry_after=_parse_retry_after(resp))
        if resp.status_code >= 400:
            raise LLMError(f"Azure OpenAI HTTP {resp.status_code}: {resp.text[:300]}")
        data = resp.json()
        try:
            return data['choices'][0]['message']['content'] or ''
        except (KeyError, IndexError) as e:
            raise LLMError(f"Azure OpenAI beklenmedik yanit sekli: {data}") from e

    # ── Gemini ───────────────────────────────────────────────────────────
    def _generate_gemini(self, system, messages, temperature, max_tokens) -> str:
        import google.generativeai as genai

        api_key = os.getenv('GEMINI_API_KEY', '')
        if not api_key:
            try:
                from django.conf import settings
                api_key = getattr(settings, 'GEMINI_API_KEY', '')
            except Exception:
                pass
        if not api_key:
            raise LLMError("GEMINI_API_KEY yapilandirilmamis")
        genai.configure(api_key=api_key)

        model = genai.GenerativeModel(
            model_name=os.getenv('GEMINI_MODEL', 'gemini-2.0-flash'),
            system_instruction=system,
        )
        contents = [
            {'role': 'model' if m['role'] == 'assistant' else 'user',
             'parts': [{'text': m['content']}]}
            for m in messages
        ]
        try:
            resp = model.generate_content(
                contents,
                generation_config={'temperature': temperature,
                                   'max_output_tokens': max_tokens},
            )
        except Exception as e:
            err = str(e)
            if '429' in err or 'quota' in err.lower() or 'rate' in err.lower():
                raise _RetryableError(err[:200], retry_after=15) from e
            raise
        return resp.text or ''


class _RetryableError(Exception):
    def __init__(self, msg, retry_after=None):
        super().__init__(msg)
        self.retry_after = retry_after


def _parse_retry_after(resp) -> int | None:
    try:
        return int(resp.headers.get('Retry-After', ''))
    except (TypeError, ValueError):
        return None
