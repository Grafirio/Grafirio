"""
Planner: kullanicinin sorusunu bagimsiz gorevlere boler.

Tek LLM cagrisi + pydantic dogrulama (llm/structured.py). Keyword tabanli
intent siniflandirmasinin yerini alir.
"""
import json
import logging

from llm.client import LLMClient
from llm.schemas import Plan
from llm.structured import generate_structured

logger = logging.getLogger(__name__)

PLANNER_SYSTEM = (
    "Sen Grafirio adlı bir İş Zekası (BI) platformunun soru planlayıcısısın. "
    "Kullanıcının doğal dildeki isteğini bağımsız görevlere bölersin.\n"
    "SADECE aşağıdaki JSON formatında yanıt ver, başka hiçbir şey yazma:\n"
    '{"tasks":[{"kind":"sql_chart|forecast|narrative","question_fragment":"<görevin tam açıklaması>",'
    '"title":"<grafik/metin başlığı>","chart_type":"bar|line|area|pie|doughnut|radar|scatter","horizon":12}]}\n\n'
    "Kurallar:\n"
    "- Sorudaki HER bağımsız veri isteği için ayrı bir görev üret; tek istekli soruya tek görev.\n"
    "- Toplam görev sayısı en fazla 5.\n"
    "- Geçmiş veriden grafik/tablo istekleri: kind=sql_chart.\n"
    "- Gelecek dönem tahmini/projeksiyon istekleri (gelecek yıl, önümüzdeki aylar, tahmin, forecast): kind=forecast; "
    "horizon alanına istenen ay sayısını yaz (yıl istenirse 12 ile çarp, en fazla 36).\n"
    "- Kullanıcı yorum/özet/açıklama istiyorsa VEYA 2+ görev varsa listenin SONUNA bir kind=narrative görevi ekle "
    "(question_fragment: sonuçların nasıl yorumlanacağı).\n"
    "- Veri gerektirmeyen genel/bilgi sorusu ise tek narrative görevi üret.\n"
    "- question_fragment kendi başına anlaşılır olsun; soru bağlamındaki dönem/adet gibi kısıtları içersin.\n"
    "- chart_type'ı kullanıcı belirttiyse ona uy; belirtmediyse veri şekline en uygun tipi seç."
)

# Few-shot ornekleri: kullanicinin gercek bilesik ornegi + tek gorevli + salt metin
FEW_SHOTS = [
    {
        'q': "Bana bu yılın bilançosu, gelecek yıllara yönelik karlılık hesaplaması ve en çok satan 10 ürünün çizgi grafiğini getir.",
        'plan': {
            "tasks": [
                {"kind": "sql_chart",
                 "question_fragment": "Bu yılın aylık bilanço özeti: aylık toplam gelir, toplam maliyet ve net kar",
                 "title": "Bu Yılın Bilanço Özeti", "chart_type": "bar", "horizon": 12},
                {"kind": "forecast",
                 "question_fragment": "Aylık net karlılığın gelecek 12 ay için projeksiyonu (tarihsel aylık kar serisinden)",
                 "title": "Karlılık Projeksiyonu (12 Ay)", "chart_type": "line", "horizon": 12},
                {"kind": "sql_chart",
                 "question_fragment": "En çok satan 10 ürün, satış miktarına göre",
                 "title": "En Çok Satan 10 Ürün", "chart_type": "line", "horizon": 12},
                {"kind": "narrative",
                 "question_fragment": "Bilanço, karlılık projeksiyonu ve en çok satan ürünler birlikte değerlendirilerek kısa bir yönetici özeti yaz",
                 "title": "Genel Değerlendirme", "chart_type": "bar", "horizon": 12},
            ]
        },
    },
    {
        'q': "En çok satan 5 ürünü pasta grafiği olarak göster",
        'plan': {
            "tasks": [
                {"kind": "sql_chart",
                 "question_fragment": "En çok satan 5 ürün, satış miktarına göre",
                 "title": "En Çok Satan 5 Ürün", "chart_type": "pie", "horizon": 12},
            ]
        },
    },
    {
        'q': "Churn analizi nedir, ne işe yarar?",
        'plan': {
            "tasks": [
                {"kind": "narrative",
                 "question_fragment": "Churn analizinin ne olduğunu ve e-ticarette ne işe yaradığını kısaca açıkla",
                 "title": "Churn Analizi", "chart_type": "bar", "horizon": 12},
            ]
        },
    },
]


def build_plan(question: str, history: list[dict], llm: LLMClient, db_schema: str) -> Plan:
    """history: [{'role': 'user'|'assistant', 'content': str}] — son turlar."""
    messages = []
    for shot in FEW_SHOTS:
        messages.append({'role': 'user', 'content': f"Soru: {shot['q']}"})
        messages.append({'role': 'assistant',
                         'content': json.dumps(shot['plan'], ensure_ascii=False)})

    context_block = ''
    if history:
        lines = [f"- {h['role']}: {h['content'][:300]}" for h in history[-6:]]
        context_block = "Önceki konuşma (bağlam için):\n" + "\n".join(lines) + "\n\n"

    messages.append({'role': 'user', 'content':
        f"{db_schema}\n\n{context_block}Soru: {question}\n\n"
        "Bu soruyu görevlere böl. Sadece JSON döndür."})

    plan = generate_structured(llm, PLANNER_SYSTEM, messages, Plan, max_tokens=1500)
    logger.info("Plan uretildi | %d gorev | %s", len(plan.tasks),
                [f"{t.kind}:{t.title}" for t in plan.tasks])
    return plan
