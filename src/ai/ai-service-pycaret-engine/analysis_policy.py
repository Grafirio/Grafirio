"""Platform defaults, loaded from the same contract exposed by the API."""
import json
from pathlib import Path

GENERAL_POLICY = json.loads(Path(__file__).with_name("analysis_policy.json").read_text(encoding="utf-8"))


def forecast_horizon(requested):
    try:
        requested = int(requested)
    except (TypeError, ValueError, OverflowError):
        requested = GENERAL_POLICY["defaultHorizon"]
    effective = max(1, min(requested, GENERAL_POLICY["maxHorizon"]))
    notice = None
    if requested > effective:
        duration = f"{requested // 12} yıl" if requested % 12 == 0 else f"{requested} ay"
        notice = f"{duration} için tahmin istediniz. Grafirio'da gelecek tahminleri en fazla 1 yıl (12 ay) ile sınırlıdır; bu nedenle 12 aylık tahmin gösteriliyor."
    return requested, effective, notice
