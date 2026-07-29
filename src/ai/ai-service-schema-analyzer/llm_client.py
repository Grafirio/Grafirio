"""
Saglayici-bagimsiz LLM istemcisi (schema-analyzer surumu).

LLM_PROVIDER env degiskeni ile secilir:
  - azure_openai : Azure OpenAI chat-completions deployment'i
  - gemini       : google-generativeai (eski varsayilan)

grafirio-ai-service/llm/client.py ile ayni sozlesmeyi izler; iki servis ayri
imajlar oldugu icin kod paylasilmiyor, davranis bilerek ayni tutuluyor.
"""
import logging
import os

import requests

logger = logging.getLogger(__name__)


class LLMError(RuntimeError):
    """LLM cagrisi basarisiz oldugunda firlatilir."""


def _is_unsupported_param(resp) -> bool:
    """gpt-5 ailesi 'max_tokens' ve temperature=0 icin 400/unsupported_* doner."""
    try:
        code = (resp.json().get('error') or {}).get('code', '')
    except ValueError:
        return False
    return code in ('unsupported_parameter', 'unsupported_value')


class LLMClient:
    # Deployment adindan model ailesi anlasilmadigi icin once modern govde
    # denenir, sunucu reddederse klasige dusulur ve karar hatirlanir.
    _use_legacy_params: bool | None = None

    def __init__(self, provider: str | None = None):
        self.provider = (provider or os.getenv('LLM_PROVIDER', 'gemini')).strip().lower()
        if self.provider not in ('azure_openai', 'gemini'):
            raise LLMError(f"Bilinmeyen LLM_PROVIDER: {self.provider}")

        self.available = True
        if self.provider == 'gemini' and not os.getenv('GEMINI_API_KEY'):
            logger.warning("GEMINI_API_KEY yok — LLM devre disi (mock mod)")
            self.available = False
        if self.provider == 'azure_openai' and not os.getenv('AZURE_OPENAI_API_KEY'):
            logger.warning("AZURE_OPENAI_API_KEY yok — LLM devre disi (mock mod)")
            self.available = False

    def generate(self, prompt: str, temperature: float = 0.2, max_tokens: int = 2048) -> str:
        if not self.available:
            raise LLMError("LLM yapilandirilmamis")
        if self.provider == 'azure_openai':
            return self._generate_azure(prompt, temperature, max_tokens)
        return self._generate_gemini(prompt, temperature, max_tokens)

    # ── Azure OpenAI ─────────────────────────────────────────────────────
    def _azure_payload(self, prompt, temperature, max_tokens, legacy: bool) -> dict:
        payload = {'messages': [{'role': 'user', 'content': prompt}]}
        if legacy:
            payload['temperature'] = temperature
            payload['max_tokens'] = max_tokens
        else:
            # Reasoning token'lari da bu butceden dusuluyor; dar tavan
            # gorunur cevabi bos birakiyor.
            payload['max_completion_tokens'] = max(max_tokens, 2048) + 2048
        return payload

    def _generate_azure(self, prompt, temperature, max_tokens) -> str:
        endpoint = os.getenv('AZURE_OPENAI_ENDPOINT', '').rstrip('/')
        deployment = os.getenv('AZURE_OPENAI_DEPLOYMENT', '')
        api_key = os.getenv('AZURE_OPENAI_API_KEY', '')
        api_version = os.getenv('AZURE_OPENAI_API_VERSION', '2024-12-01-preview')
        if not (endpoint and deployment and api_key):
            raise LLMError("AZURE_OPENAI_ENDPOINT / AZURE_OPENAI_DEPLOYMENT / AZURE_OPENAI_API_KEY eksik")

        url = f"{endpoint}/openai/deployments/{deployment}/chat/completions?api-version={api_version}"
        attempts = [False, True] if LLMClient._use_legacy_params is None \
            else [LLMClient._use_legacy_params]

        resp = None
        for legacy in attempts:
            resp = requests.post(
                url,
                json=self._azure_payload(prompt, temperature, max_tokens, legacy),
                headers={'api-key': api_key},
                timeout=90,
            )
            if resp.status_code == 400 and _is_unsupported_param(resp) and legacy is False:
                logger.info("Azure OpenAI modern parametreleri reddetti, klasige dusuluyor")
                continue
            if resp.status_code >= 400:
                raise LLMError(f"Azure OpenAI HTTP {resp.status_code}: {resp.text[:300]}")
            LLMClient._use_legacy_params = legacy
            break

        data = resp.json()
        try:
            content = data['choices'][0]['message']['content'] or ''
        except (KeyError, IndexError) as e:
            raise LLMError(f"Azure OpenAI beklenmedik yanit sekli: {data}") from e

        if not content.strip():
            raise LLMError(
                f"Azure OpenAI bos icerik dondurdu (usage={data.get('usage', {})})"
            )
        return content

    # ── Gemini ───────────────────────────────────────────────────────────
    def _generate_gemini(self, prompt, temperature, max_tokens) -> str:
        import google.generativeai as genai

        genai.configure(api_key=os.getenv('GEMINI_API_KEY', ''))
        model = genai.GenerativeModel(os.getenv('GEMINI_MODEL', 'gemini-2.0-flash'))
        response = model.generate_content(
            prompt,
            generation_config={
                'temperature': temperature,
                'max_output_tokens': max_tokens,
            },
        )
        return (response.text or '').strip()
