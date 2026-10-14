import unittest
from datetime import date
from unittest.mock import patch
from query_spec import PeriodBucket, ColumnRef, Predicate
from forecast_history import complete_month_history
from test_analysis_report import dictionary
from test_forecast import AgentAnalyzer, TimePort, FakeForecasterModule


class CompleteMonthTests(unittest.TestCase):
    def test_rolling_two_year_scope_is_narrowed_without_widening(self):
        axis = PeriodBucket('t0', 'CreatedDate', 'month')
        predicates = [Predicate(ColumnRef('t0', 'CreatedDate'), '>=', ['p0']),
                      Predicate(ColumnRef('t0', 'CreatedDate'), '<', ['p1']),
                      Predicate(ColumnRef('t0', 'Country'), '=', ['p2'])]
        original = {'p0': '2024-10-14', 'p1': '2026-10-15', 'p2': 'DE'}
        narrowed, values, policy = complete_month_history(axis, predicates, original, date(2026, 10, 14))
        self.assertEqual(policy['fromInclusive'], '2024-11-01')
        self.assertEqual(policy['toExclusive'], '2026-10-01')
        self.assertEqual(policy['excludedPartialMonths'], ['2024-10', '2026-10'])
        self.assertEqual(values['p2'], 'DE')
        self.assertEqual(narrowed[:3], predicates)
        self.assertEqual(original, {'p0': '2024-10-14', 'p1': '2026-10-15', 'p2': 'DE'})

    def test_full_month_edges_are_retained_and_inclusive_end_is_conservative(self):
        axis = PeriodBucket('t0', 'CreatedDate', 'month')
        predicates = [Predicate(ColumnRef('t0', 'CreatedDate'), '>=', ['a']), Predicate(ColumnRef('t0', 'CreatedDate'), '<', ['b'])]
        _, _, policy = complete_month_history(axis, predicates, {'a': '2024-10-01', 'b': '2026-10-01'}, date(2026, 10, 14))
        self.assertEqual(policy['excludedPartialMonths'], [])
        self.assertEqual(policy['fromInclusive'], '2024-10-01')
        predicates[1] = Predicate(ColumnRef('t0', 'CreatedDate'), '<=', ['b'])
        _, _, policy = complete_month_history(axis, predicates, {'a': '2024-10-01', 'b': '2026-09-30'}, date(2026, 10, 14))
        self.assertEqual(policy['toExclusive'], '2026-09-01')

    def test_model_receives_only_complete_months_and_splits_report_tables(self):
        rows = [{'Donem': f'{2024 + (9 + i) // 12}-{(9 + i) % 12 + 1:02}', 'value': float(100 + i)} for i in range(25)]
        fake = FakeForecasterModule()
        params = {'target_table': 'dbo.Calculations', 'target_column': 'Amount', 'analysis_type': 'forecast',
                  'aggregation': 'sum', 'horizon': 12, 'time_axis': {'column': 'CreatedDate', 'granularity': 'month'},
                  'filters': {'CreatedDate': {'gte': '2024-10-14', 'lt': '2026-10-15'}}}
        port = TimePort(rows)
        with patch('forecast_history.today_utc', return_value=date(2026, 10, 14)), patch.dict('sys.modules', {'forecaster': fake}):
            result = AgentAnalyzer(port).run_analysis(dictionary(), params)
        self.assertTrue(result['success'], result)
        self.assertEqual(len(fake.calls[0]['series']), 23)
        self.assertEqual(fake.calls[0]['series'][0]['period'], '2024-11')
        self.assertEqual(fake.calls[0]['series'][-1]['period'], '2026-09')
        self.assertEqual(result['report']['dataPeriod'], {'from': '2024-11', 'to': '2026-09'})
        self.assertEqual([t['kind'] for t in result['report']['tables']], ['history', 'forecast'])
        self.assertEqual([len(t['rows']) for t in result['report']['tables']], [23, 12])
        self.assertIn('2026-10-01', port.queries[0][1].values())

    def test_monthly_averages_are_not_added_as_an_annual_total(self):
        rows = [{'Donem': f'2025-{m:02}', 'value': float(m)} for m in range(1, 13)]
        params = {'target_table': 'dbo.Calculations', 'target_column': 'Amount', 'analysis_type': 'forecast',
                  'aggregation': 'avg', 'horizon': 12, 'time_axis': {'column': 'CreatedDate', 'granularity': 'month'}}
        with patch.dict('sys.modules', {'forecaster': FakeForecasterModule()}), patch('forecast_history.today_utc', return_value=date(2026, 1, 15)):
            result = AgentAnalyzer(TimePort(rows)).run_analysis(dictionary(), params)
        self.assertTrue(result['success'], result)
        self.assertIsNone(result['report']['forecastSummary']['total'])

    def test_missing_recent_month_does_not_produce_a_forecast_for_the_past(self):
        rows = [{'Donem': f'2025-{m:02}', 'value': float(m)} for m in range(1, 13)]
        fake = FakeForecasterModule()
        params = {'target_table': 'dbo.Calculations', 'target_column': 'Amount', 'analysis_type': 'forecast',
                  'aggregation': 'sum', 'horizon': 12, 'time_axis': {'column': 'CreatedDate', 'granularity': 'month'}}
        with patch.dict('sys.modules', {'forecaster': fake}), patch('forecast_history.today_utc', return_value=date(2026, 10, 14)):
            result = AgentAnalyzer(TimePort(rows)).run_analysis(dictionary(), params)
        self.assertFalse(result['success'])
        self.assertEqual(fake.calls, [])
        self.assertIn('2026-09', result['error'])
        self.assertEqual(result['audit']['errorCode'], 'incomplete_history')
