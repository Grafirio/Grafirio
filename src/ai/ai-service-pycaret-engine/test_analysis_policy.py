import unittest
from forecaster import ForecastError, forecast_series


class GeneralPolicyTests(unittest.TestCase):
    def series(self):
        return [{"period": f"2025-{m:02}", "value": m * 10.0} for m in range(1, 13)]

    def test_direct_forecast_cannot_bypass_one_year_limit(self):
        result = forecast_series(self.series(), horizon=60)
        self.assertEqual(len(result["forecast"]), 12)
        self.assertEqual(result["horizon"], 12)
        self.assertEqual(result["requested_horizon"], 60)
        self.assertIn("5 yıl", result["notices"][0])
        self.assertEqual(result["forecast"][-1]["period"], "2026-12")
        self.assertEqual(forecast_series(self.series(), horizon=12)["notices"], [])

    def test_direct_forecast_rejects_short_missing_and_nonfinite_history(self):
        short = self.series()[:-1]
        missing = self.series()
        missing[5]["period"] = "2026-01"
        nonfinite = self.series()
        nonfinite[5]["value"] = float("inf")
        for rows in (short, missing, nonfinite):
            with self.subTest(rows=rows), self.assertRaises(ForecastError):
                forecast_series(rows, horizon=60)
