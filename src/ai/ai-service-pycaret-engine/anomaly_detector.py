"""
Zaman serisi sapma tespiti — donem serisinde beklenenden ayrilan noktalar.

forecaster ile ayni model merdivenini kullanir (Holt-Winters -> Holt ->
lineer trend) ama gelecegi degil GECMISIN beklenenini uretir: artiklarin
standart sapmasina gore esigi asan donemler isaretlenir.

numpy/statsmodels modul seviyesinde ICE AKTARILMAZ: lineer yol saf
Python'dur ve kutuphanesiz ortamda (birim testleri dahil) calisir;
mevsimsel/egilim yollari kutuphane varsa denenir, yoksa lineere dusulur —
uretim imajinda ikisi de kurulu, dusus yalnizca kisa/uyumsuz serilerde olur.
"""
import logging
import math
from statistics import pstdev

logger = logging.getLogger(__name__)


class AnomalyError(ValueError):
    """Gecersiz girdi veya tespit yapilamayan durumlar."""


#: Artigin kac standart sapma otesi "sapma" sayilir. 2σ gurultuyu isaretler,
#: 3σ gercek sicramalari kacirabilir; 2.5 BI serileri icin dengeli.
DEFAULT_THRESHOLD = 2.5

#: Daha azinda "beklenen" diye bir sey yok: model seriyi ezberler ve artiklar
#: anlamsizlasir.
MIN_POINTS = 8


def _linear_fit(values: list) -> list:
    """En kucuk kareler dogrusu — saf Python, her zaman calisir."""
    n = len(values)
    mean_x = (n - 1) / 2.0
    mean_y = sum(values) / n
    sxx = sum((x - mean_x) ** 2 for x in range(n))
    sxy = sum((x - mean_x) * (values[x] - mean_y) for x in range(n))
    slope = sxy / sxx if sxx else 0.0
    intercept = mean_y - slope * mean_x
    return [intercept + slope * x for x in range(n)]


def _fitted_values(values: list, seasonality) -> tuple:
    """Beklenen degerler + kullanilan model adi, gucluden zayifa merdivenle."""
    n = len(values)

    if seasonality and n >= 2 * seasonality:
        try:
            from statsmodels.tsa.holtwinters import ExponentialSmoothing
            model = ExponentialSmoothing(
                values, trend='add', seasonal='add',
                seasonal_periods=seasonality,
                initialization_method='estimated',
            ).fit()
            return [float(v) for v in model.fittedvalues], 'HoltWinters'
        except Exception as error:
            logger.warning("Holt-Winters basarisiz, Holt'a dusuluyor: %s", error)

    if n >= MIN_POINTS:
        try:
            from statsmodels.tsa.holtwinters import Holt
            model = Holt(values, initialization_method='estimated').fit()
            return [float(v) for v in model.fittedvalues], 'Holt'
        except Exception as error:
            logger.warning("Holt basarisiz, lineer trende dusuluyor: %s", error)

    return _linear_fit(values), 'LinearTrend'


def detect_anomalies(series: list, seasonality=12,
                     threshold: float = DEFAULT_THRESHOLD) -> dict:
    """
    series: [{'period': 'yyyy-MM', 'value': float}, ...] (sirasiz olabilir)
    Doner: {'model_name', 'sigma',
            'expected':  [{'period','value'}...],
            'anomalies': [{'period','actual','expected','score'}...]}

    `score` = artik / sigma; isareti yonu soyler (+ sicrama, - cokme).
    Sigma sifirsa seri sabittir ve sapma tanim geregi yoktur.
    """
    clean = []
    for point in series:
        try:
            value = float(point['value'])
            if not math.isnan(value):
                clean.append({'period': str(point['period']), 'value': value})
        except (KeyError, TypeError, ValueError):
            continue
    clean.sort(key=lambda point: point['period'])

    if len(clean) < MIN_POINTS:
        raise AnomalyError(
            f"Sapma analizi için en az {MIN_POINTS} dönem gerekir "
            f"({len(clean)} verildi)")

    values = [point['value'] for point in clean]
    fitted, model_name = _fitted_values(values, seasonality)
    residuals = [value - expected for value, expected in zip(values, fitted)]
    sigma = pstdev(residuals)

    anomalies = []
    if sigma > 0:
        for point, expected, residual in zip(clean, fitted, residuals):
            score = residual / sigma
            if abs(score) > threshold:
                anomalies.append({
                    'period': point['period'],
                    'actual': round(point['value'], 2),
                    'expected': round(expected, 2),
                    'score': round(score, 1),
                })

    logger.info("Sapma taramasi | model=%s | donem=%d | sapma=%d",
                model_name, len(clean), len(anomalies))
    return {
        'model_name': model_name,
        'sigma': round(sigma, 4),
        'expected': [{'period': point['period'], 'value': round(expected, 2)}
                     for point, expected in zip(clean, fitted)],
        'anomalies': anomalies,
    }
