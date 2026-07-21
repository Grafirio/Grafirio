"""
Zaman serisi tahmini — /forecast endpoint'inin motoru.

Kisa aylik BI serileri (12-36 nokta) icin statsmodels merdiveni kullanilir:
  Holt-Winters (>= 2*seasonality nokta) -> Holt (trend) -> OLS lineer trend (<8 nokta)
statsmodels pycaret 3.x'in bagimliligidir; imaj buyumez, sonuc deterministiktir
ve milisaniyeler icinde doner.
"""
import logging
import math
import re

import numpy as np

logger = logging.getLogger(__name__)


class ForecastError(ValueError):
    """Gecersiz girdi veya tahmin uretilemedigi durumlar."""


def _next_periods(last_period: str, horizon: int) -> list[str]:
    """'yyyy-MM' formatindaki donemi ilerletir; taninmayan formatta 'T+n' uretir."""
    m = re.fullmatch(r'(\d{4})-(\d{2})', str(last_period).strip())
    if m:
        year, month = int(m.group(1)), int(m.group(2))
        periods = []
        for _ in range(horizon):
            month += 1
            if month > 12:
                month = 1
                year += 1
            periods.append(f"{year:04d}-{month:02d}")
        return periods
    return [f"T+{i + 1}" for i in range(horizon)]


def forecast_series(series: list[dict], horizon: int = 12,
                    seasonality: int | None = 12) -> dict:
    """
    series: [{'period': 'yyyy-MM', 'value': float}, ...] (sirali)
    Doner: {'forecast': [{'period','value','lower','upper'}...], 'model_name': str}
    """
    clean = []
    for p in series:
        try:
            v = float(p['value'])
            if not math.isnan(v):
                clean.append({'period': str(p['period']), 'value': v})
        except (KeyError, TypeError, ValueError):
            continue
    clean.sort(key=lambda p: p['period'])

    if len(clean) < 4:
        raise ForecastError(f"En az 4 veri noktasi gerekir ({len(clean)} verildi)")
    horizon = max(1, min(int(horizon), 36))

    values = np.array([p['value'] for p in clean], dtype=float)
    n = len(values)

    fitted = None
    predictions = None
    model_name = None

    # 1) Holt-Winters (mevsimsel) — yeterli veri varsa
    if seasonality and n >= 2 * seasonality:
        try:
            from statsmodels.tsa.holtwinters import ExponentialSmoothing
            model = ExponentialSmoothing(
                values, trend='add', seasonal='add',
                seasonal_periods=seasonality,
                initialization_method='estimated',
            ).fit()
            fitted, predictions = model.fittedvalues, model.forecast(horizon)
            model_name = 'HoltWinters'
        except Exception as e:
            logger.warning("Holt-Winters basarisiz, Holt'a dusuluyor: %s", e)

    # 2) Holt (trend, mevsimsellik yok)
    if predictions is None and n >= 8:
        try:
            from statsmodels.tsa.holtwinters import Holt
            model = Holt(values, initialization_method='estimated').fit()
            fitted, predictions = model.fittedvalues, model.forecast(horizon)
            model_name = 'Holt'
        except Exception as e:
            logger.warning("Holt basarisiz, lineer trende dusuluyor: %s", e)

    # 3) OLS lineer trend — her zaman calisir
    if predictions is None:
        x = np.arange(n, dtype=float)
        slope, intercept = np.polyfit(x, values, 1)
        fitted = intercept + slope * x
        future_x = np.arange(n, n + horizon, dtype=float)
        predictions = intercept + slope * future_x
        model_name = 'LinearTrend'

    # Guven araligi: residual std, ufukla genisler (±1.96σ·√h)
    residuals = values - np.asarray(fitted)[:n]
    sigma = float(np.std(residuals)) if n > 2 else 0.0

    periods = _next_periods(clean[-1]['period'], horizon)
    forecast = []
    for i, (period, value) in enumerate(zip(periods, predictions)):
        band = 1.96 * sigma * math.sqrt(i + 1)
        forecast.append({
            'period': period,
            'value': round(float(value), 2),
            'lower': round(float(value) - band, 2),
            'upper': round(float(value) + band, 2),
        })

    logger.info("Forecast uretildi | model=%s | girdi=%d nokta | ufuk=%d",
                model_name, n, horizon)
    return {'forecast': forecast, 'model_name': model_name}
