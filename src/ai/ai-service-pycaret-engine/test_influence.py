"""Key-influencer ranking and presentation (pure helpers, no pycaret)."""

import importlib
import sys
import types
import unittest
from unittest.mock import patch

# Keep the production environment dependency-free without leaking a pandas stub.
with patch.dict(sys.modules, {
    "pandas": types.SimpleNamespace(DataFrame=object, isna=lambda value: value is None),
}):
    AgentAnalyzer = importlib.import_module("agent_analyzer").AgentAnalyzer


class RankInfluenceTests(unittest.TestCase):
    rank = staticmethod(AgentAnalyzer._rank_influence)

    def test_tree_importances_are_normalized_and_sorted(self):
        ranked = self.rank(["a", "b", "c"], importances=[1.0, 3.0, 0.0])
        self.assertEqual(ranked, [("b", 75.0), ("a", 25.0), ("c", 0.0)])

    def test_linear_coefficients_are_scaled_by_std(self):
        # Buyuk olcekli kolon kucuk katsayi alir; std ile carpmadan siralama
        # yalan soyler: |2|*1=2 < |-1|*10=10.
        ranked = self.rank(["kucuk", "buyuk"], coef=[2.0, -1.0], stds=[1.0, 10.0])
        self.assertEqual(ranked[0][0], "buyuk")
        self.assertAlmostEqual(ranked[0][1], 83.3)
        self.assertAlmostEqual(ranked[1][1], 16.7)

    def test_multiclass_coefficients_average_over_classes(self):
        ranked = self.rank(["f1", "f2"], coef=[[1.0, -1.0], [3.0, 1.0]])
        self.assertEqual(ranked, [("f1", 66.7), ("f2", 33.3)])

    def test_uninspectable_models_yield_none(self):
        for kwargs in [
            {},                                      # ne importance ne coef
            {"importances": [0.0, 0.0]},             # agirlik yok
            {"importances": [1.0]},                  # uzunluk tutmuyor
        ]:
            with self.subTest(kwargs=kwargs):
                self.assertIsNone(self.rank(["a", "b"], **kwargs))


class FakeSeries:
    def __init__(self, std=1.0, corr_to_target=0.0):
        self.std_value = std
        self.corr_to_target = corr_to_target

    def std(self):
        return self.std_value

    def corr(self, other):
        return other.corr_to_target


class ExtractInfluenceTests(unittest.TestCase):
    def setUp(self):
        self.analyzer = AgentAnalyzer(None)
        self.frame = {
            "hedef": FakeSeries(),
            "fiyat": FakeSeries(std=2.0, corr_to_target=0.8),
            "adet": FakeSeries(std=1.0, corr_to_target=-0.3),
            "kur": FakeSeries(std=1.0, corr_to_target=0.01),
        }

    def test_linear_model_ranks_with_direction_hints(self):
        model = types.SimpleNamespace(coef_=[3.0, 4.0, 1.0])
        ranked, directions = self.analyzer._extract_influence(
            model, ["fiyat", "adet", "kur"], self.frame, "hedef")
        self.assertEqual([name for name, _ in ranked], ["fiyat", "adet", "kur"])
        # Yon ipucu korelasyon isaretinden; |corr| < 0.05 olan sessiz kalir.
        self.assertEqual(directions, {"fiyat": 1, "adet": -1})

    def test_model_without_introspection_degrades_quietly(self):
        ranked, directions = self.analyzer._extract_influence(
            types.SimpleNamespace(), ["fiyat"], self.frame, "hedef")
        self.assertIsNone(ranked)
        self.assertEqual(directions, {})

    def test_raising_model_never_breaks_analysis(self):
        class Hostile:
            @property
            def coef_(self):
                raise RuntimeError("boom")

        ranked, directions = self.analyzer._extract_influence(
            Hostile(), ["fiyat"], self.frame, "hedef")
        self.assertIsNone(ranked)
        self.assertEqual(directions, {})


class PresentationTests(unittest.TestCase):
    def test_answer_leads_with_influencers_not_metrics(self):
        analyzer = AgentAnalyzer(None)
        quality = {"type": "info", "title": "Model güveni", "description": "R² 0.9"}
        chart, insights = analyzer._influence_presentation(
            "Ciro", [("Fiyat", 60.0), ("Adet", 30.0), ("Kur", 10.0)],
            {"Fiyat": 1, "Adet": -1}, quality, rows=100, feature_count=3)

        self.assertEqual(chart["type"], "bar")
        self.assertEqual(chart["data"]["labels"], ["Fiyat", "Adet", "Kur"])
        self.assertEqual(chart["data"]["datasets"][0]["data"], [60.0, 30.0, 10.0])

        text = str(insights)
        self.assertIn("Fiyat: etki payı %60.0", text)
        self.assertIn("arttıkça hedef artıyor", text)
        self.assertIn("arttıkça hedef azalıyor", text)
        self.assertIn("R² 0.9", text)
        self.assertIn("nedensellik", text)
        self.assertIn("100 satır, 3 özellik", text)


if __name__ == "__main__":
    unittest.main()
